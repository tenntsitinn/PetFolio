using System;
using System.IO;

static class BubbleSettingsTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    static void Main()
    {
        try
        {
            Run();
            Console.WriteLine(checks + " bubble settings and content checks passed");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("After " + checks + " checks: " + e.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PetFolio-settings-中文 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "appearance.json");
        int errors = 0;
        string lastError = "";
        var store = new BubbleSettingsStore(path, (k, d) =>
        {
            errors++;
            lastError += new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
        });
        try
        {
            var settings = store.Load();
            Check(settings.QuotaEnabled && settings.SystemEnabled && !settings.SystemMode, "Missing settings enable both modes and select quota");
            Check(settings.DefaultAppearance && settings.OpacityPercent == 90 && !settings.DefaultFrostedGlass && settings.PetFrostedGlass, "Missing settings retain appearance defaults");
            File.WriteAllText(path, "{\"opacityPercent\":20,\"panelCorner\":\"bottom-start\"}");
            settings = store.Load();
            Check(settings.OpacityPercent == 20 && settings.PanelCorner == "left" && settings.QuotaEnabled && settings.SystemEnabled, "Legacy appearance settings load with default feature flags");
            store.Save(settings);
            Check(store.Load().PanelCorner == "left", "Migrated left side survives saving and reloading");
            File.WriteAllText(path, "{\"opacityPercent\":200,\"panelCorner\":\"bad\",\"quotaEnabled\":\"bad\",\"systemEnabled\":false,\"appearanceMode\":\"pet\"}");
            settings = store.Load();
            Check(settings.OpacityPercent == 100 && settings.PanelCorner == "right" && settings.QuotaEnabled && !settings.SystemEnabled && !settings.DefaultAppearance, "Invalid field types fall back independently and opacity is clamped");
            File.WriteAllText(path, "{");
            settings = store.Load();
            Check(settings.QuotaEnabled && settings.SystemEnabled && errors == 1, "Malformed settings fall back and report a diagnostic");
            File.WriteAllText(path, "null");
            settings = store.Load();
            Check(settings.DefaultAppearance && errors == 2, "Null settings fall back safely");
            settings = new BubbleSettings
            {
                OpacityPercent = 10,
                PanelCorner = "bottom-end",
                SystemMode = true,
                QuotaEnabled = false,
                SystemEnabled = true,
                DefaultAppearance = false,
                DefaultFrostedGlass = true,
                PetFrostedGlass = false
            };
            store.Save(settings);
            var restored = store.Load();
            Check(restored.OpacityPercent == 10 && restored.PanelCorner == "right" && restored.SystemMode && !restored.QuotaEnabled && restored.SystemEnabled,
                "Mode, switches and placement round-trip: " + File.ReadAllText(path) + " (errors=" + errors + ", last=" + lastError + ")");
            Check(!restored.DefaultAppearance && restored.DefaultFrostedGlass && !restored.PetFrostedGlass, "Independent appearance choices round-trip");
            settings.OpacityPercent = 30;
            store.Save(settings);
            Check(store.Load().OpacityPercent == 30, "Saving replaces an existing settings file");
            string before = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                settings.OpacityPercent = 40;
                store.Save(settings);
            }

            Check(errors == 3 && File.ReadAllText(path) == before, "Failed replacement reports an error and preserves the complete previous file");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Success and failed writes leave no temporary settings files");
            var state = new BubbleContentState(true, true, false);
            Check(!state.SystemMode, "Unavailable saved System mode falls back to Quota");
            state = new BubbleContentState(false, false, true);
            Check(state.SystemMode, "Unavailable saved Quota mode falls back to System");
            Check(state.Select(false) == state, "Disabled mode selection is rejected");
            state = state.Enable(true, true);
            Check(state.SystemMode, "Enabling the other mode preserves selection");
            state = state.Select(false);
            Check(!state.SystemMode, "An enabled mode can be selected");
            state = state.Enable(false, false);
            Check(!state.AnyEnabled && !state.SystemMode, "Both disabled retain selection with no enabled content");
            state = state.Enable(false, true);
            Check(state.SystemMode && state.AnyEnabled, "Re-enabling content selects the available mode");
            state.CopyTo(settings);
            Check(settings.SystemMode && !settings.QuotaEnabled && settings.SystemEnabled, "Persisted flags come from the authoritative content state");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
