// Immutable state supplied to the view by the feature; no window dependencies.
sealed class BubbleContentState
{
    internal readonly bool SystemMode, QuotaEnabled, SystemEnabled;
    internal bool AnyEnabled
    {
        get
        {
            return QuotaEnabled || SystemEnabled;
        }
    }

    internal BubbleContentState(bool systemMode, bool quotaEnabled, bool systemEnabled)
    {
        QuotaEnabled = quotaEnabled;
        SystemEnabled = systemEnabled;
        SystemMode = systemMode;
        if (SystemMode && !SystemEnabled && QuotaEnabled)
            SystemMode = false;
        if (!SystemMode && !QuotaEnabled && SystemEnabled)
            SystemMode = true;
    }

    internal BubbleContentState(BubbleSettings settings) : this(settings.SystemMode, settings.QuotaEnabled, settings.SystemEnabled)
    {
    }

    internal BubbleContentState Select(bool system)
    {
        return (system ? !SystemEnabled : !QuotaEnabled) || SystemMode == system ? this : new BubbleContentState(system, QuotaEnabled, SystemEnabled);
    }

    internal BubbleContentState Enable(bool quota, bool system)
    {
        if ((!quota && !system) || (quota == QuotaEnabled && system == SystemEnabled))
            return this;
        return new BubbleContentState(SystemMode, quota, system);
    }

    internal void CopyTo(BubbleSettings settings)
    {
        settings.SystemMode = SystemMode;
        settings.QuotaEnabled = QuotaEnabled;
        settings.SystemEnabled = SystemEnabled;
    }
}
