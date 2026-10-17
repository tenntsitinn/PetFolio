using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

// Owns the shared bubble; each enabled mode controls its own data resources.
sealed class QuotaFeature : ICompanionFeature
{
    readonly string home;
    readonly IPetStateSource pets;
    readonly Func<IQuotaSource> createSource;
    readonly Action<Action> dispatch;
    readonly Action<QuotaSnapshot> persist;
    readonly Action<string, object> record;
    readonly BubbleSettingsStore settingsStore;
    readonly Func<ISystemMetricsSource> createSystemSource;
    readonly Action refreshDisplayMetrics;
    BubbleSettings settings;
    BubbleContentState content;
    QuotaLabel view;
    QuotaService quota;
    SystemMetricsService system;
    ToolStripMenuItem refresh;
    readonly ToolStripMenuItem quotaOption = new ToolStripMenuItem("Quota remaining");
    readonly ToolStripMenuItem systemOption = new ToolStripMenuItem("System usage");
    bool disposed;
    public string Name
    {
        get
        {
            return "Bubble";
        }
    }

    public bool IsRunning
    {
        get
        {
            return view != null;
        }
    }

    public ToolStripItem[] Commands { get; private set; }

    public ToolStripItem[] FeatureOptions
    {
        get
        {
            return new ToolStripItem[]
            {
                quotaOption,
                systemOption
            };
        }
    }

    internal QuotaLabel View
    {
        get
        {
            return view;
        }
    }

    internal SystemMetricsService SystemService
    {
        get
        {
            return system;
        }
    }

    public QuotaFeature(string home, IPetStateSource pets, Func<IQuotaSource> createSource,
        Action<Action> dispatch, Action<QuotaSnapshot> persist, Action<string, object> record,
        BubbleSettingsStore settingsStore = null, Func<ISystemMetricsSource> createSystemSource = null,
        Action refreshDisplayMetrics = null)
    {
        this.home = home;
        this.pets = pets;
        this.createSource = createSource;
        this.dispatch = dispatch;
        this.persist = persist;
        this.record = record;
        this.settingsStore = settingsStore ?? new BubbleSettingsStore(Path.Combine(Program.DataFolder, "appearance.json"), record);
        this.createSystemSource = createSystemSource ?? (() => new SystemMetricsSource());
        this.refreshDisplayMetrics = refreshDisplayMetrics;
        Commands = new ToolStripItem[0];
        quotaOption.Click += (s, e) => ToggleMode(false);
        systemOption.Click += (s, e) => ToggleMode(true);
    }

    public void Start()
    {
        if (disposed)
            throw new ObjectDisposedException("QuotaFeature");
        if (IsRunning)
            return;
        try
        {
            settings = settingsStore.Load();
            content = new BubbleContentState(settings);
            view = new QuotaLabel(home, settings, content);
            system = new SystemMetricsService(createSystemSource, record);
            system.Changed += OnSystem;
            var handle = view.Handle;
            view.RefreshRequested += OnRefreshRequested;
            view.ModeRequested += SelectMode;
            view.SettingsChanged += SaveSettings;
            view.SamplingVisibilityChanged += UpdateSystemSampling;
            view.DisplayMetricsChanged += refreshDisplayMetrics;
            pets.Changed += OnPet;
            refresh = new ToolStripMenuItem("Refresh", null, (s, e) => view.RefreshCurrent());
            var commands = new List<ToolStripItem>(view.Commands);
            commands.Add(refresh);
            Commands = commands.ToArray();
            view.UpdatePet(pets.Current);
            UpdateQuotaService();
            UpdateSystemSampling();
            UpdateOptions();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    void OnPet(PetState state)
    {
        if (view != null)
            view.UpdatePet(state);
    }

    void OnQuota(QuotaState state)
    {
        if (view != null)
            view.Present(state);
    }

    void OnSystem(SystemMetrics metrics, bool manual)
    {
        if (view != null)
            view.PresentSystem(metrics, manual);
    }

    void OnRefreshRequested()
    {
        if (content.SystemMode)
        {
            if (content.SystemEnabled && system != null)
                system.Refresh();
        }
        else if (quota != null)
            quota.Refresh();
    }

    void UpdateSystemSampling()
    {
        if (system != null)
            system.SetRunning(view != null && content.SystemEnabled && content.SystemMode && view.Visible && view.BubbleRequested);
    }

    void SaveSettings()
    {
        if (view == null)
            return;
        view.CopyAppearanceTo(settings);
        content.CopyTo(settings);
        settingsStore.Save(settings);
    }

    void SelectMode(bool systemMode)
    {
        if (view == null)
            return;
        var nextContent = content.Select(systemMode);
        if (nextContent == content)
            return;
        SetContent(nextContent);
        SaveSettings();
    }

    void SetContent(BubbleContentState nextContent)
    {
        bool modeChanged = content.SystemMode != nextContent.SystemMode;
        content = nextContent;
        view.ApplyContent(content);
        UpdateSystemSampling();
        UpdateOptions();
        if (modeChanged)
            record("bubble-mode", new { mode = content.SystemMode ? "system" : "quota" });
    }

    void UpdateQuotaService()
    {
        if (content.QuotaEnabled && quota == null)
        {
            var nextQuotaService = new QuotaService(createSource(), dispatch, persist, record);
            try
            {
                nextQuotaService.Changed += OnQuota;
                nextQuotaService.Start();
                quota = nextQuotaService;
            }
            catch
            {
                nextQuotaService.Changed -= OnQuota;
                nextQuotaService.Dispose();
                throw;
            }
        }
        else if (!content.QuotaEnabled && quota != null)
        {
            quota.Changed -= OnQuota;
            quota.Dispose();
            quota = null;
        }
    }

    void UpdateOptions()
    {
        quotaOption.Checked = view != null && content.QuotaEnabled;
        systemOption.Checked = view != null && content.SystemEnabled;
        if (refresh != null)
            refresh.Enabled = content.AnyEnabled;
    }

    void ToggleMode(bool systemMode)
    {
        if (view == null)
            return;
        var previousContent = content;
        try
        {
            SetContent(content.Enable(
                systemMode ? content.QuotaEnabled : !content.QuotaEnabled,
                systemMode ? !content.SystemEnabled : content.SystemEnabled));
            UpdateQuotaService();
            SaveSettings();
        }
        catch (Exception ex)
        {
            SetContent(previousContent);
            record("feature-error", new { feature = systemMode ? "System usage" : "Quota remaining", error = ex.GetType().Name });
            MessageBox.Show("Unable to change this feature.", "PetFolio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateOptions();
    }

    public void Restore()
    {
        if (!IsRunning)
            Start();
        view.SetBubbleVisible(true);
    }

    internal void SetTrayMenuOpen(bool open)
    {
        if (view != null)
            view.SetTrayMenuOpen(open);
    }

    public void Stop()
    {
        pets.Changed -= OnPet;
        if (system != null)
        {
            system.Changed -= OnSystem;
            system.Dispose();
            system = null;
        }

        if (quota != null)
        {
            quota.Changed -= OnQuota;
            quota.Dispose();
            quota = null;
        }

        if (view != null)
        {
            view.RefreshRequested -= OnRefreshRequested;
            view.ModeRequested -= SelectMode;
            view.SettingsChanged -= SaveSettings;
            view.SamplingVisibilityChanged -= UpdateSystemSampling;
            view.DisplayMetricsChanged -= refreshDisplayMetrics;
            view.Close();
            view.Dispose();
            view = null;
        }

        if (refresh != null)
        {
            refresh.Dispose();
            refresh = null;
        }

        Commands = new ToolStripItem[0];
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Stop();
        quotaOption.Dispose();
        systemOption.Dispose();
    }
}
