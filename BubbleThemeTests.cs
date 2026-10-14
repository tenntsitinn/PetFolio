using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Actual Codex config, quota timer and Bubble click handlers, with a fake source.
static class BubbleThemeTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }

    static Color Surface(QuotaLabel view)
    {
        return (Color)Field(view, "panelColor");
    }

    sealed class Pets : IPetStateSource
    {
        public PetState Current
        {
            get
            {
                return new PetState(new IntPtr(1), new Point(600, 400), 112, true, true, false, "theme", Color.Gold);
            }
        }

        public event Action<PetState> Changed
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    sealed class Source : IQuotaSource
    {
        internal bool Fail;
        public QuotaSnapshot Read()
        {
            if (Fail)
                throw new IOException("Offline");
            return new QuotaSnapshot(DateTime.UtcNow, new QuotaWindow(20, 300, null), null);
        }

        public void Dispose()
        {
        }
    }

    static void Tick(object timer)
    {
        typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(timer, new object[] { EventArgs.Empty });
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Run();
            Console.WriteLine(checks + " Bubble theme checks passed");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        string home = Path.Combine(Path.GetTempPath(), "PetFolio-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        string config = Path.Combine(home, "config.toml");
        var pending = new ConcurrentQueue<Action>();
        var source = new Source();
        try
        {
            File.WriteAllText(config, "appearanceTheme = \"dark\"\n");
            using (var posted = new AutoResetEvent(false))
            using (var feature = new QuotaFeature(home, new Pets(), () => source, a =>
            {
                pending.Enqueue(a);
                posted.Set();
            }, s =>
            {
            }, (k, d) =>
            {
            }))
            {
                Action drain = () =>
                {
                    Check(posted.WaitOne(5000), "Quota completes");
                    Action a;
                    while (pending.TryDequeue(out a))
                        a();
                };
                feature.Start();
                drain();
                var view = feature.View;
                bool? system = false;
                view.ReadSystemDark = () => system;
                view.SetDefaultAppearance(true);
                view.SetMode(false);
                Check(Surface(view) == BubbleAppearance.Surface(true), "Codex dark overrides light Windows");
                var service = Field(feature, "quota");
                var timer = Field(service, "refresh");
                File.WriteAllText(config, "appearanceTheme = \"light\"\n");
                system = true;
                Tick(timer);
                Check(Surface(view) == BubbleAppearance.Surface(false), "Automatic quota query syncs Codex light before completion even with dark Windows");
                drain();
                File.WriteAllText(config, "appearanceTheme = \"dark\"\n");
                source.Fail = true;
                var mouse = new MouseEventArgs(MouseButtons.Left, 1, 80, 40, 0);
                typeof(QuotaLabel).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { mouse });
                typeof(QuotaLabel).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { mouse });
                Tick(Field(view, "clickTimer"));
                Check(Surface(view) == BubbleAppearance.Surface(true), "Bubble single click syncs Codex dark");
                drain();
                Check(Surface(view) == BubbleAppearance.Surface(true), "Quota failure does not undo the theme change");
                source.Fail = false;
                File.WriteAllText(config, "appearanceTheme = \"light\"\n");
                using (var locked = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Tick(timer);
                    Check(Surface(view) == BubbleAppearance.Surface(true), "Locked config retains last theme");
                    drain();
                }

                Tick(timer);
                Check(Surface(view) == BubbleAppearance.Surface(false), "Next quota cycle retries after config unlocks");
                drain();
                File.WriteAllText(config, "appearanceTheme = \"system\"\n");
                system = true;
                Tick(timer);
                Check(Surface(view) == BubbleAppearance.Surface(true), "Codex system selection follows Windows");
                drain();
                system = null;
                Tick(timer);
                Check(Surface(view) == BubbleAppearance.Surface(true), "Failed system reading retains the palette");
                drain();
                view.SetDefaultAppearance(false);
                var pet = Surface(view);
                File.WriteAllText(config, "appearanceTheme = \"light\"\n");
                Tick(timer);
                Check(Surface(view) == pet, "Quota theme checks preserve Pet colour");
                drain();
                view.SetDefaultAppearance(true);
                view.SetMode(true);
                File.WriteAllText(config, "appearanceTheme = \"dark\"\n");
                view.RefreshCurrent();
                Check(Surface(view) == BubbleAppearance.Surface(true), "System mode manual refresh also syncs Codex theme");
                File.WriteAllText(config, "# theme temporarily unavailable\n");
                system = false;
                Tick(timer);
                Check(Surface(view) == BubbleAppearance.Surface(true), "Missing Codex theme retains its last palette instead of switching to light Windows");
                drain();
                File.WriteAllText(config, "appearanceTheme = \"unknown\"\n");
                view.RefreshCurrent();
                Check(Surface(view) == BubbleAppearance.Surface(true), "Manual refresh retains palette for unknown Codex theme");
                view.SetBubbleVisible(false);
                File.WriteAllText(config, "appearanceTheme = \"light\"\n");
                Tick(timer);
                Check(!view.Visible && Surface(view) == BubbleAppearance.Surface(false), "Hidden Bubble syncs quota theme without becoming visible");
                drain();
            }
        }
        finally
        {
            File.Delete(config);
            Directory.Delete(home);
        }
    }
}
