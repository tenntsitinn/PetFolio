using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// The message loop belongs to the host, not to any feature's window.
sealed class CompanionApplication : ApplicationContext {
    readonly Control dispatcher = new Control();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly System.Windows.Forms.Timer signals = new System.Windows.Forms.Timer();
    readonly List<ICompanionFeature> features = new List<ICompanionFeature>();
    readonly Dictionary<ICompanionFeature, ToolStripMenuItem> toggles = new Dictionary<ICompanionFeature, ToolStripMenuItem>();
    readonly ToolStripMenuItem featureMenu = new ToolStripMenuItem("Features");
    readonly ToolStripMenuItem exit;
    readonly ToolStripMenuItem autoStart = new ToolStripMenuItem("Start when Codex opens");
    readonly ToolStripSeparator separator = new ToolStripSeparator();
    PetStateService pets;
    Icon applicationIcon;
    volatile bool disposed;
    public CompanionApplication(string executable,string home,EventWaitHandle stop,EventWaitHandle show) {
        exit=new ToolStripMenuItem("Exit",null,(s,e)=>{CodexAutoStart.Suppress(Program.DataFolder);ExitThread();});
        autoStart.Checked=CodexAutoStart.Enabled(Program.DataFolder);
        autoStart.Click+=(s,e)=> {
            try {
                if(autoStart.Checked)CodexAutoStart.Disable(Program.DataFolder);
                else CodexAutoStart.Enable(Program.DataFolder,home,System.Reflection.Assembly.GetExecutingAssembly().Location);
                autoStart.Checked=CodexAutoStart.Enabled(Program.DataFolder);
            }catch(Exception ex){MessageBox.Show(ex.Message,"PetFolio 自動啟動設定失敗",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        };
        try {
            var handle=dispatcher.Handle;
            pets=new PetStateService(home,Path.Combine(Program.DataFolder,"pet-palettes.json"),Dispatch);
            var quota=new QuotaFeature(home,pets,()=>new CodexQuotaSource(executable),Dispatch,
                snapshot=>File.WriteAllText(Path.Combine(Program.DataFolder,"quota-snapshot.json"),
                    new JavaScriptSerializer().Serialize(snapshot.ToRecord())),Program.Record);
            Register(quota);
            pets.Start();quota.Start();RebuildMenu();
            using(var stream=typeof(CompanionApplication).Assembly.GetManifestResourceStream("PetFolio.Icon"))
            using(var embeddedIcon=new Icon(stream,32,32))applicationIcon=(Icon)embeddedIcon.Clone();
            tray.Icon=applicationIcon;tray.Text="PetFolio";tray.ContextMenuStrip=menu;tray.Visible=true;
            if(!pets.Current.Visible) tray.ShowBalloonTip(5000,"PetFolio","請在 Codex 開啟 Pet；寵物出現後會自動顯示額度氣泡。",ToolTipIcon.Info);
            signals.Interval=100;
            signals.Tick+=(s,e)=> {
                if(stop.WaitOne(0)){ExitThread();return;}
                if(show.WaitOne(0)){quota.Restore();RebuildMenu();}
            };
            var desktopTicks=0;
            signals.Tick+=(s,e)=> {
                if(disposed)return;
                if(++desktopTicks<20)return;desktopTicks=0;
                // Close the companion after Codex exits; only the watcher remains.
                if(autoStart.Checked && CodexDesktopSession.Current().Length==0)ExitThread();
            };
            signals.Start();
            Program.Record("started",new {pollSeconds=300,anchorPollMilliseconds=100,dragTickMilliseconds=16,accountSource="Codex CLI login"});
        }catch {Release();throw;}
    }
    void Dispatch(Action callback) {
        if(disposed)return;
        try {dispatcher.BeginInvoke(new Action(()=>{if(!disposed)callback();}));}
        catch(InvalidOperationException) {if(!disposed)throw;}
    }
    // Add future in-process features here through the same lifecycle contract.
    void Register(ICompanionFeature feature) {
        features.Add(feature);
        var toggle=new ToolStripMenuItem(feature.Name);
        toggle.Click+=(s,e)=> {
            try {if(feature.IsRunning)feature.Stop();else feature.Start();}
            catch(Exception ex) {
                Program.Record("feature-error",new {feature=feature.Name,error=ex.GetType().Name});
                tray.ShowBalloonTip(5000,feature.Name,"Unable to start this feature.",ToolTipIcon.Error);
            }
            RebuildMenu();
        };
        toggles.Add(feature,toggle);featureMenu.DropDownItems.Add(toggle);
    }
    void RebuildMenu() {
        menu.Items.Clear();
        foreach(var feature in features) {
            toggles[feature].Checked=feature.IsRunning;
            if(feature.IsRunning)menu.Items.AddRange(feature.Commands);
        }
        menu.Items.Add(separator);menu.Items.Add(featureMenu);menu.Items.Add(autoStart);menu.Items.Add(exit);
    }
    void Release() {
        if(disposed)return;disposed=true;
        signals.Stop();signals.Dispose();
        for(int i=features.Count-1;i>=0;i--)features[i].Dispose();
        if(pets!=null)pets.Dispose();
        tray.Visible=false;tray.Dispose();menu.Dispose();
        if(applicationIcon!=null)applicationIcon.Dispose();
        featureMenu.Dispose();separator.Dispose();autoStart.Dispose();exit.Dispose();dispatcher.Dispose();
        Program.Record("stopped",new { });
    }
    protected override void ExitThreadCore() {Release();base.ExitThreadCore();}
    protected override void Dispose(bool disposing) {if(disposing)Release();base.Dispose(disposing);}
}
