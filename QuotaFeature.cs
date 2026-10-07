using System;
using System.Collections.Generic;
using System.Windows.Forms;

// Owns only quota resources. A hidden view keeps polling; Stop releases both.
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
    bool disposed;
    public string Name { get { return "Quota Bubble"; } }
    public bool IsRunning { get { return view != null; } }
    public ToolStripItem[] Commands { get; private set; }
    internal QuotaLabel View { get { return view; } }
    public QuotaFeature(string home, IPetStateSource pets, Func<IQuotaSource> createSource,
        Action<Action> dispatch, Action<QuotaSnapshot> persist, Action<string, object> record) {
        this.home=home;this.pets=pets;this.createSource=createSource;
        this.dispatch=dispatch;this.persist=persist;this.record=record;
        Commands=new ToolStripItem[0];
    }
    public void Start() {
        if(disposed)throw new ObjectDisposedException("QuotaFeature");
        if(IsRunning)return;
        try {
            view=new QuotaLabel(home);
            var handle=view.Handle;
            quota=new QuotaService(createSource(),dispatch,persist,record);
            quota.Changed+=OnQuota;
            view.RefreshRequested+=OnRefreshRequested;
            pets.Changed+=OnPet;
            refresh=new ToolStripMenuItem("Refresh quota",null,(s,e)=>quota.Refresh());
            var commands=new List<ToolStripItem>(view.Commands);commands.Add(refresh);Commands=commands.ToArray();
            view.UpdatePet(pets.Current);
            quota.Start();
        }catch {Stop();throw;}
    }
    void OnPet(PetState state) { if(view!=null)view.UpdatePet(state); }
    void OnQuota(QuotaState state) { if(view!=null)view.Present(state); }
    void OnRefreshRequested() { if(quota!=null)quota.Refresh(); }
    public void Restore() { if(!IsRunning)Start();view.SetBubbleVisible(true); }
    public void Stop() {
        pets.Changed-=OnPet;
        if(quota!=null){quota.Changed-=OnQuota;quota.Dispose();quota=null;}
        if(view!=null){view.RefreshRequested-=OnRefreshRequested;view.Close();view.Dispose();view=null;}
        if(refresh!=null){refresh.Dispose();refresh=null;}
        Commands=new ToolStripItem[0];
    }
    public void Dispose() { if(disposed)return;disposed=true;Stop(); }
}
