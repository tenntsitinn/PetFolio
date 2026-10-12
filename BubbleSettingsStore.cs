using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

// Persisted preferences; runtime mode transitions belong to BubbleContentState.
sealed class BubbleSettings {
    internal int OpacityPercent=90;
    internal string PanelCorner="top-end";
    internal bool SystemMode,QuotaEnabled=true,SystemEnabled=true;
    internal bool DefaultAppearance=true,DefaultFrostedGlass,PetFrostedGlass=true;
}

sealed class BubbleSettingsStore {
    readonly string path;
    readonly Action<string,object> record;
    internal BubbleSettingsStore(string path,Action<string,object> record) {this.path=path;this.record=record;}
    internal BubbleSettings Load() {
        var settings=new BubbleSettings();
        try {
            if(!File.Exists(path))return settings;
            var values=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            if(values==null)throw new ArgumentException("Invalid bubble settings");
            object value;
            if(values.TryGetValue("opacityPercent",out value) && value is int)
                settings.OpacityPercent=Math.Max(10,Math.Min(100,(int)value));
            if(values.TryGetValue("panelCorner",out value) && PetPanelPlacement.Valid(value as string))settings.PanelCorner=(string)value;
            if(values.TryGetValue("bubbleMode",out value))settings.SystemMode=(value as string)=="system";
            if(values.TryGetValue("appearanceMode",out value))settings.DefaultAppearance=(value as string)!="pet";
            settings.QuotaEnabled=ReadBool(values,"quotaEnabled",true);
            settings.SystemEnabled=ReadBool(values,"systemEnabled",true);
            settings.DefaultFrostedGlass=ReadBool(values,"defaultFrostedGlass",false);
            settings.PetFrostedGlass=ReadBool(values,"petFrostedGlass",true);
        }catch(IOException e){RecordError(e);}catch(UnauthorizedAccessException e){RecordError(e);}
        catch(ArgumentException e){RecordError(e);}catch(InvalidOperationException e){RecordError(e);}
        return settings;
    }
    static bool ReadBool(Dictionary<string,object> values,string key,bool fallback) {
        object value;return values.TryGetValue(key,out value) && value is bool?(bool)value:fallback;
    }
    internal void Save(BubbleSettings settings) {
        // Replace a complete file so interrupted writes cannot truncate preferences.
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(new {
                opacityPercent=settings.OpacityPercent,panelCorner=settings.PanelCorner,bubbleMode=settings.SystemMode?"system":"quota",
                quotaEnabled=settings.QuotaEnabled,systemEnabled=settings.SystemEnabled,appearanceMode=settings.DefaultAppearance?"default":"pet",
                defaultFrostedGlass=settings.DefaultFrostedGlass,petFrostedGlass=settings.PetFrostedGlass }));
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }catch(IOException e){RecordError(e);}catch(UnauthorizedAccessException e){RecordError(e);}
        finally {
            try {if(File.Exists(temporary))File.Delete(temporary);}
            catch(IOException e){RecordError(e);}catch(UnauthorizedAccessException e){RecordError(e);}
        }
    }
    void RecordError(Exception e) {record("appearance-error",new {error=e.GetType().Name,message=e.Message});}
}
