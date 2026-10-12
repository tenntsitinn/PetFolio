using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

// Diagnostics must never prevent a refresh, interaction or clean shutdown.
sealed class DiagnosticLog {
    readonly object gate=new object();
    readonly Func<string> directory;
    readonly Func<long> milliseconds;
    readonly long maxBytes;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly UTF8Encoding utf8=new UTF8Encoding(false);
    long retryAt;
    internal long DroppedEvents {get;private set;}
    internal string LastError {get;private set;}
    internal DiagnosticLog(Func<string> directory,long maxBytes=2*1024*1024,Func<long> milliseconds=null) {
        this.directory=directory;this.maxBytes=maxBytes;
        var clock=Stopwatch.StartNew();this.milliseconds=milliseconds ?? (()=>clock.ElapsedMilliseconds);
    }
    internal bool Write(string kind,object data) {
        lock(gate) {
            if(milliseconds()<retryAt){DroppedEvents++;return false;}
            try {
                string line=json.Serialize(new {at=DateTime.UtcNow.ToString("o"),kind=kind,data=data})+"\n";
                long bytes=utf8.GetByteCount(line);
                if(bytes>maxBytes){DroppedEvents++;LastError="EventTooLarge";return false;}
                string root=directory();Directory.CreateDirectory(root);
                string path=Path.Combine(root,"probe-events.jsonl");
                if(File.Exists(path) && new FileInfo(path).Length+bytes>maxBytes) {
                    string previous=Path.Combine(root,"probe-events.previous.jsonl");
                    if(File.Exists(previous))File.Delete(previous);
                    File.Move(path,previous);
                }
                File.AppendAllText(path,line,utf8);LastError=null;return true;
            }catch(IOException e){Fail(e);}catch(UnauthorizedAccessException e){Fail(e);}
            catch(ArgumentException e){Fail(e);}catch(InvalidOperationException e){Fail(e);}
            catch(NotSupportedException e){Fail(e);}
            return false;
        }
    }
    void Fail(Exception error){DroppedEvents++;LastError=error.GetType().Name;retryAt=milliseconds()+5000;}
}
