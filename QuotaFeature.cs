using System;
using System.Collections.Generic;
using System.Windows.Forms;

// Owns the shared bubble; each enabled mode controls its own data resources.
sealed class QuotaFeature : ICompanionFeature {
    readonly string home;
    readonly IPetStateSource pets;
    readonly Func<IQuotaSource> createSource;
    readonly Action<Action> dispatch;
    readonly Action<QuotaSnapshot> persist;
    readonly Action<string, object> record;
    QuotaLabel view;
    QuotaService quota;
    ToolStripMenuItem refresh;
    readonly ToolStripMenuItem quotaOption=new ToolStripMenuItem("Quota remaining");
    readonly ToolStripMenuItem systemOption=new ToolStripMenuItem("System usage");
    bool disposed;
    public string Name { get { return "Bubble"; } }
    public bool IsRunning { get { return view != null; } }
    public ToolStripItem[] Commands { get; private set; }
    public ToolStripItem[] FeatureOptions { get { return new ToolStripItem[]{quotaOption,systemOption}; } }
    internal QuotaLabel View { get { return view; } }
    public QuotaFeature(string home, IPetStateSource pets, Func<IQuotaSource> createSource,
        Action<Action> dispatch, Action<QuotaSnapshot> persist, Action<string, object> record) {
        this.home=home;this.pets=pets;this.createSource=createSource;
        this.dispatch=dispatch;this.persist=persist;this.record=record;
        Commands=new ToolStripItem[0];
        quotaOption.Click+=(s,e)=>ToggleMode(false);
        systemOption.Click+=(s,e)=>ToggleMode(true);
    }
    public void Start() {
        if(disposed)throw new ObjectDisposedException("QuotaFeature");
        if(IsRunning)return;
        try {
            view=new QuotaLabel(home);
            var handle=view.Handle;
            view.RefreshRequested+=OnRefreshRequested;
            pets.Changed+=OnPet;
            refresh=new ToolStripMenuItem("Refresh",null,(s,e)=>view.RefreshCurrent());
            var commands=new List<ToolStripItem>(view.Commands);commands.Add(refresh);Commands=commands.ToArray();
            view.UpdatePet(pets.Current);
            UpdateQuotaService();UpdateOptions();
        }catch {Stop();throw;}
    }
    void OnPet(PetState state) { if(view!=null)view.UpdatePet(state); }
    void OnQuota(QuotaState state) { if(view!=null)view.Present(state); }
    void OnRefreshRequested() { if(quota!=null)quota.Refresh(); }
    void UpdateQuotaService() {
        if(view.QuotaEnabled && quota==null) {
            quota=new QuotaService(createSource(),dispatch,persist,record);
            quota.Changed+=OnQuota;quota.Start();
        }else if(!view.QuotaEnabled && quota!=null) {
            quota.Changed-=OnQuota;quota.Dispose();quota=null;
        }
    }
    void UpdateOptions() {
        quotaOption.Checked=view!=null && view.QuotaEnabled;
        systemOption.Checked=view!=null && view.SystemEnabled;
        if(refresh!=null)refresh.Enabled=view.AnyModeEnabled;
    }
    void ToggleMode(bool system) {
        if(view==null)return;
        bool oldQuota=view.QuotaEnabled,oldSystem=view.SystemEnabled;
        try {
            view.SetEnabledModes(system?oldQuota:!oldQuota,system?!oldSystem:oldSystem);
            UpdateQuotaService();
        }catch(Exception ex) {
            view.SetEnabledModes(oldQuota,oldSystem);
            record("feature-error",new {feature=system?"System usage":"Quota remaining",error=ex.GetType().Name});
            MessageBox.Show("Unable to change this feature.","PetFolio",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        UpdateOptions();
    }
    public void Restore() { if(!IsRunning)Start();view.SetBubbleVisible(true); }
    internal void SetTrayMenuOpen(bool open) {if(view!=null)view.SetTrayMenuOpen(open);}
    public void Stop() {
        pets.Changed-=OnPet;
        if(quota!=null){quota.Changed-=OnQuota;quota.Dispose();quota=null;}
        if(view!=null){view.RefreshRequested-=OnRefreshRequested;view.Close();view.Dispose();view=null;}
        if(refresh!=null){refresh.Dispose();refresh=null;}
        Commands=new ToolStripItem[0];
    }
    public void Dispose() { if(disposed)return;disposed=true;Stop();quotaOption.Dispose();systemOption.Dispose(); }
}
