"""Build a small RGB catalog from local sprite sources; no images or credentials are exported.
Requires Pillow and NumPy only when regenerating this catalog, not when running the probe.
"""
import argparse, io, json, os, struct
from pathlib import Path
import numpy as np
from PIL import Image


def dominant(image):
    rgba=np.asarray(image.convert('RGBA'))[::2,::2].reshape(-1,4).astype(float)
    rgb=rgba[:,:3];maximum=rgb.max(axis=1);minimum=rgb.min(axis=1)
    keep=(rgba[:,3]>=32)&(maximum>=35);rgb=rgb[keep];maximum=maximum[keep];minimum=minimum[keep]
    if len(rgb)<20:raise ValueError('No visible pet pixels')
    delta=maximum-minimum;saturation=delta/maximum;vivid=saturation>=.18
    if not vivid.any():return np.mean(rgb,axis=0).astype(int).tolist()
    colours=rgb[vivid];hi=maximum[vivid];diff=delta[vivid];sat=saturation[vivid]
    hue=np.zeros(len(colours));r,g,b=colours.T
    mask=hi==r;hue[mask]=((g[mask]-b[mask])/diff[mask])%6
    mask=(hi==g)&(hi!=r);hue[mask]=(b[mask]-r[mask])/diff[mask]+2
    mask=(hi==b)&(hi!=r)&(hi!=g);hue[mask]=(r[mask]-g[mask])/diff[mask]+4
    bins=((hue*60+7.5)/15).astype(int)%24;weights=.35+.65*sat
    totals=np.bincount(bins,weights,minlength=24);best=int(totals.argmax())
    if totals[best]<max(12,len(rgb)*.06):return np.mean(rgb[~vivid],axis=0).astype(int).tolist()
    mask=bins==best
    return np.average(colours[mask],axis=0,weights=weights[mask]).astype(int).tolist()


def entry(image,source,asset=None):
    r,g,b=dominant(image);stat=source.stat()
    result=dict(r=r,g=g,b=b,sourcePath=str(source),sourceBytes=stat.st_size,
                lastWriteUtcTicks=str(stat.st_mtime_ns//100+621355968000000000))
    if asset:result['asset']=asset
    return result


def find(value,key):
    if not isinstance(value,dict):return None
    if key in value:return value[key]
    for child in value.values():
        found=find(child,key)
        if found is not None:return found


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--asar',required=True,type=Path)
    parser.add_argument('--home',required=True,type=Path);args=parser.parse_args();entries={}
    with args.asar.open('rb') as stream:
        header=struct.unpack('<4I',stream.read(16));index=json.loads(stream.read(header[3]));base=8+header[1]
        assets=index['files']['webview']['files']['assets']['files']
        for name,metadata in assets.items():
            if '-spritesheet-' not in name or not name.endswith('.webp'):continue
            pet=name.split('-spritesheet-',1)[0];stream.seek(base+int(metadata['offset']))
            with Image.open(io.BytesIO(stream.read(int(metadata['size'])))) as image:
                entries[pet]=entry(image,args.asar,name)
    for manifest in (args.home/'pets').glob('*/pet.json'):
        data=json.loads(manifest.read_text(encoding='utf-8-sig'))
        sprite=(manifest.parent/data['spritesheetPath']).resolve()
        if sprite.parent!=manifest.parent.resolve():continue
        with Image.open(sprite) as image:entries['custom:'+data['id']]=entry(image,sprite)
    state=json.loads((args.home/'.codex-global-state.json').read_text(encoding='utf-8-sig'))
    def aliases(value):
        if not isinstance(value,dict):return
        for key,child in value.items():
            if key in entries and isinstance(child,str):entries[child]=entries[key].copy()
            elif isinstance(child,dict):aliases(child)
    aliases(find(state,'migrated-cloud-pet-ids-v1'))
    destination=Path(__file__).with_name('pet-palettes.json');temporary=destination.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(dict(version=1,entries=entries),indent=2),encoding='utf-8');os.replace(temporary,destination)
    for pet,value in entries.items():print(pet, '#%02X%02X%02X'%(value['r'],value['g'],value['b']))

if __name__=='__main__':main()
