using System;
using System.IO;
using System.Text.RegularExpressions;

static class CodexMenuTheme {
    static readonly Regex setting=new Regex("^\\s*appearanceTheme\\s*=\\s*[\"'](dark|light|system)[\"']\\s*(?:#.*)?$");
    internal static bool? Read(string home,Func<bool?> systemDark) {
        try {
            if(!String.IsNullOrEmpty(home)) {
                string path=Path.Combine(home,"config.toml");
                if(File.Exists(path))foreach(string line in File.ReadLines(path)) {
                    var match=setting.Match(line);if(!match.Success)continue;
                    string mode=match.Groups[1].Value;
                    if(mode=="dark")return true;if(mode=="light")return false;
                    return systemDark();
                }
            }
            return systemDark();
        }catch(IOException){return null;}
        catch(UnauthorizedAccessException){return null;}
        catch(System.Security.SecurityException){return null;}
    }
}
