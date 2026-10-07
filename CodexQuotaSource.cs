using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// All CLI protocol/process ownership stays behind IQuotaSource.
sealed class CodexQuotaSource : IQuotaSource {
    readonly string executable;
    readonly object gate = new object();
    Process process;
    bool disposed;
    public CodexQuotaSource(string executable) { this.executable = executable; }
    public QuotaSnapshot Read() {
        var json = new JavaScriptSerializer();
        using (var p = new Process()) {
            p.StartInfo = new ProcessStartInfo(executable, "app-server --stdio") {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            try {
                lock (gate) {
                    if (disposed) throw new ObjectDisposedException("CodexQuotaSource");
                    if (process != null) throw new InvalidOperationException("A quota request is already running");
                    p.Start(); process = p;
                }
                p.ErrorDataReceived += (s, e) => { }; p.BeginErrorReadLine();
                p.StandardInput.WriteLine(json.Serialize(new { id = 1, method = "initialize",
                    @params = new { clientInfo = new { name = "PetFolio", version = "0.2.0" } } }));
                p.StandardInput.Flush();
                bool initialized = false;
                var timeout = Stopwatch.StartNew();
                while (timeout.ElapsedMilliseconds < 30000) {
                    var pending = Task.Factory.StartNew(() => p.StandardOutput.ReadLine());
                    if (!pending.Wait(Math.Max(1, 30000 - (int)timeout.ElapsedMilliseconds)))
                        throw new TimeoutException("Quota request timed out");
                    var line = pending.Result;
                    if (line == null) throw new InvalidOperationException("Codex CLI exited before response");
                    Dictionary<string, object> message;
                    try { message = json.Deserialize<Dictionary<string, object>>(line); }
                    catch (ArgumentException) { continue; }
                    object id;
                    if (message == null || !message.TryGetValue("id", out id)) continue;
                    int responseId;
                    if (!Int32.TryParse(Convert.ToString(id), out responseId) || responseId != (initialized ? 2 : 1)) continue;
                    if (message.ContainsKey("error")) throw new InvalidOperationException("Codex rejected quota request");
                    if (!initialized) {
                        initialized = true;
                        p.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                        p.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                        p.StandardInput.Flush();
                    } else return QuotaResponse.Parse(QuotaResponse.Dict(message, "result"), DateTime.UtcNow);
                }
                throw new TimeoutException("Quota request timed out");
            } finally {
                lock (gate) {
                    try { p.StandardInput.Close(); } catch (InvalidOperationException) { }
                    catch (System.IO.IOException) { }
                    Kill(p);
                    if (process == p) process = null;
                }
            }
        }
    }
    static void Kill(Process p) {
        try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
    public void Dispose() {
        lock (gate) {
            if (disposed) return;
            disposed = true; if (process != null) Kill(process);
        }
    }
}
