using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

// Every observed selection receives its own generation, including A -> B -> A.
// Stale asynchronous results cannot complete or reset a newer selection.
sealed class PetColourSwitch {
    public string Pet {get;private set;}
    public long Generation {get;private set;}
    public bool HasColour {get;private set;}
    public long NextSampleAt {get;private set;}
    long changedAt;
    Color candidate;
    int votes;
    public PetColourSwitch(){Pet="";}
    public bool Select(string pet,long now) {
        if(String.IsNullOrEmpty(pet) || pet==Pet)return false;
        Pet=pet;Generation++;HasColour=false;votes=0;changedAt=now;NextSampleAt=now+350;return true;
    }
    public bool ApplySource(long generation,Color colour) {
        if(generation!=Generation || HasColour)return false;
        HasColour=true;votes=0;return true;
    }
    public bool CanSample(long now){return !HasColour && Pet.Length>0 && now>=NextSampleAt;}
    public bool AcceptSample(long generation,Color? sample,long now) {
        if(generation!=Generation || HasColour)return false;
        NextSampleAt=now+150;
        if(!sample.HasValue){votes=0;return false;}
        var colour=sample.Value;
        if(votes>0 && PetTheme.Distance(colour,candidate)<65)votes++;else votes=1;
        candidate=colour;
        if(votes<3 || now-changedAt<750)return false;
        HasColour=true;return true;
    }
}

// Source-derived RGB only. Builtin source metadata and local sprite metadata invalidate stale entries.
// The generator uses Pillow once; the overlay has no Python runtime dependency.
sealed class PetPaletteCatalog {
    readonly string path;
    DateTime loadedAt;
    Dictionary<string,object> entries=new Dictionary<string,object>();
    public PetPaletteCatalog(string file){path=file;}
    public bool TryGet(string pet,out Color colour) {
        colour=Color.Empty;
        try {
            var modified=File.GetLastWriteTimeUtc(path);
            if(modified!=loadedAt) {
                var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
                entries=root["entries"] as Dictionary<string,object> ?? new Dictionary<string,object>();loadedAt=modified;
            }
            object value;if(!entries.TryGetValue(pet,out value))return false;
            var entry=value as Dictionary<string,object>;if(entry==null)return false;
            var source=new FileInfo(Convert.ToString(entry["sourcePath"]));
            if(!source.Exists || source.Length!=Convert.ToInt64(entry["sourceBytes"]) || source.LastWriteTimeUtc.Ticks!=Convert.ToInt64(entry["lastWriteUtcTicks"]))return false;
            colour=Color.FromArgb(Convert.ToInt32(entry["r"]),Convert.ToInt32(entry["g"]),Convert.ToInt32(entry["b"]));return true;
        }catch(IOException){return false;}catch(ArgumentException){return false;}catch(KeyNotFoundException){return false;}
        catch(FormatException){return false;}catch(OverflowException){return false;}catch(InvalidOperationException){return false;}
    }
}

static class PetSelectionConfig {
    public static string Read(string path) {
        try {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(stream)) {
                string section="",line;
                while((line=reader.ReadLine())!=null) {
                    line=line.Trim();if(line.Length==0 || line.StartsWith("#"))continue;
                    if(line.StartsWith("[")){int end=line.IndexOf(']');if(end>0)section=line.Substring(1,end-1).Trim();continue;}
                    if(section!="desktop")continue;
                    int equals=line.IndexOf('=');if(equals<0)continue;
                    if(line.Substring(0,equals).Trim().Trim('"','\'')!="selected-avatar-id")continue;
                    var value=line.Substring(equals+1).Trim();if(value.Length<2 || (value[0]!='"' && value[0]!='\''))return null;
                    int closing=value.IndexOf(value[0],1);if(closing<1)return null;
                    var trailing=value.Substring(closing+1).Trim();if(trailing.Length>0 && !trailing.StartsWith("#"))return null;
                    return value.Substring(1,closing-1);
                }
            }
        }catch(IOException){ }
        return null;
    }
}
