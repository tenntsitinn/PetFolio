using System;
using System.IO;

// Mutable state survives plugin cache replacement and portable upgrades.
static class RuntimeData {
    public static string Resolve() {
        return Resolve(Environment.GetEnvironmentVariable("PETFOLIO_DATA_DIR"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
    internal static string Resolve(string configured,string localRoot) {
        if(!String.IsNullOrWhiteSpace(configured)) {
            var root=Path.GetPathRoot(configured);
            if(!Path.IsPathRooted(configured) || root.Length<=1 || root.EndsWith(":"))
                throw new ArgumentException("PETFOLIO_DATA_DIR 必須是絕對路徑。");
            return Path.GetFullPath(configured);
        }
        return Path.Combine(localRoot,"PetFolio");
    }
    public static void Initialize(string destination,string legacyDirectory) {
        Directory.CreateDirectory(destination);
        if(String.Equals(Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(legacyDirectory).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))return;
        // Copy preferences and validated palette metadata only. Old quota and
        // diagnostic records are not needed to resume the application.
        foreach(var name in new[]{"appearance.json","pet-palettes.json"}) {
            var source=Path.Combine(legacyDirectory,name);var target=Path.Combine(destination,name);
            if(File.Exists(source) && !File.Exists(target))File.Copy(source,target,false);
        }
    }
}
