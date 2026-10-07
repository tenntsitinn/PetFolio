using System;
using System.Collections.Generic;

// Transport-independent values. Views and future alerts consume the same snapshot.
sealed class QuotaWindow {
    public double UsedPercent { get; private set; }
    public double RemainingPercent { get { return Math.Max(0, Math.Min(100, 100 - UsedPercent)); } }
    public int DurationMinutes { get; private set; }
    public long? ResetsAt { get; private set; }
    public QuotaWindow(double used, int minutes, long? resetsAt) {
        if (Double.IsNaN(used) || Double.IsInfinity(used)) throw new FormatException("Invalid quota percentage");
        UsedPercent = used; DurationMinutes = minutes; ResetsAt = resetsAt;
    }
    internal object ToRecord() {
        return new { usedPercent = UsedPercent, windowDurationMins = DurationMinutes, resetsAt = ResetsAt };
    }
}

sealed class QuotaSnapshot {
    public DateTime CheckedAt { get; private set; }
    public QuotaWindow Primary { get; private set; }
    public QuotaWindow Secondary { get; private set; }
    public string Source { get { return "Codex CLI login"; } }
    public QuotaSnapshot(DateTime checkedAt, QuotaWindow primary, QuotaWindow secondary) {
        CheckedAt = checkedAt; Primary = primary; Secondary = secondary;
    }
    public object ToRecord() {
        return new { checkedAt = CheckedAt.ToString("o"), source = Source,
            rateLimits = new { primary = Primary == null ? null : Primary.ToRecord(),
                secondary = Secondary == null ? null : Secondary.ToRecord() } };
    }
}

sealed class QuotaState {
    public QuotaSnapshot Snapshot { get; private set; }
    public string Error { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsStale { get { return Snapshot != null && Error != null; } }
    public QuotaState(QuotaSnapshot snapshot, string error, bool refreshing = false) {
        Snapshot = snapshot; Error = error; IsRefreshing = refreshing;
    }
}

static class QuotaResponse {
    internal static Dictionary<string, object> Dict(Dictionary<string, object> value, string key) {
        object result;
        return value != null && value.TryGetValue(key, out result) ? result as Dictionary<string, object> : null;
    }
    public static QuotaSnapshot Parse(Dictionary<string, object> result, DateTime checkedAt) {
        var bucket = Dict(result, "rateLimits");
        var buckets = Dict(result, "rateLimitsByLimitId");
        if (buckets != null && buckets.ContainsKey("codex")) bucket = Dict(buckets, "codex");
        if (bucket == null) throw new FormatException("Quota data unavailable for this CLI account");
        var primary = Window(Dict(bucket, "primary"));
        var secondary = Window(Dict(bucket, "secondary"));
        if (primary == null && secondary == null) throw new FormatException("Quota windows unavailable");
        return new QuotaSnapshot(checkedAt, primary, secondary);
    }
    static QuotaWindow Window(Dictionary<string, object> value) {
        if (value == null) return null;
        object used, minutes, resets;
        if (!value.TryGetValue("usedPercent", out used) || used == null) return null;
        return new QuotaWindow(Convert.ToDouble(used),
            value.TryGetValue("windowDurationMins", out minutes) && minutes != null ? Convert.ToInt32(minutes) : 0,
            value.TryGetValue("resetsAt", out resets) && resets != null ? (long?)Convert.ToInt64(resets) : null);
    }
}

interface IQuotaSource : IDisposable {
    QuotaSnapshot Read();
}
