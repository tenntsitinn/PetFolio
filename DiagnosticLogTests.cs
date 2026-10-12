using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class DiagnosticLogTests
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
        checks++;
    }

    static void Main()
    {
        try
        {
            Run();
            Console.WriteLine(checks + " diagnostic log checks passed");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "PetFolio-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            long now = 0;
            int attempts = 0;
            var log = new DiagnosticLog(() =>
            {
                attempts++;
                return root;
            }, 512, () => now);
            Check(log.Write("started", new { text = "中文" }), "First event creates a UTF-8 log");
            string path = Path.Combine(root, "probe-events.jsonl");
            Check(File.ReadAllText(path).Contains("中文"), "Unicode event data survives logging");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Check(!log.Write("locked", new { }), "Locked diagnostic file cannot throw into the application");
                int before = attempts;
                Check(!log.Write("backoff", new { }) && attempts == before, "Failed disk writes back off without retrying filesystem access");
            }

            now = 5000;
            Check(log.Write("recovered", new { }) && log.LastError == null && log.DroppedEvents == 2, "Logging recovers after the retry interval");
            Check(!log.Write("oversized", new { text = new string ('x', 1024) }), "An oversized event cannot exceed the log limit");
            Parallel.For(0, 40, i => log.Write("parallel", new { index = i }));
            string previous = Path.Combine(root, "probe-events.previous.jsonl");
            Check(File.Exists(previous) && new FileInfo(path).Length <= 512 && new FileInfo(previous).Length <= 512, "Rotation bounds current and retained logs");
            var json = new JavaScriptSerializer();
            int lines = 0;
            foreach (var file in new[]
            {
                path,
                previous
            })
                foreach (var line in File.ReadAllLines(file))
                {
                    json.DeserializeObject(line);
                    lines++;
                }

            Check(lines > 1, "Concurrent writers preserve complete JSON lines across rotations");
            var invalid = new DiagnosticLog(() =>
            {
                throw new UnauthorizedAccessException();
            }, 512, () => now);
            Check(!invalid.Write("failure", new { }) && invalid.LastError == "UnauthorizedAccessException", "Directory resolution failure is isolated from application behavior");
            Check(Directory.GetFiles(root).Length == 2, "Rotation retains exactly one previous file");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(root))
                File.Delete(file);
            Directory.Delete(root);
        }
    }
}
