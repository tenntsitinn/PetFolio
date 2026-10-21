using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

static class WeeklyQuotaTests
{
    static object Field(QuotaLabel view, string name)
    {
        return typeof(QuotaLabel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread]
    static void Main()
    {
        using (var view = new QuotaLabel("", new BubbleSettings(), new BubbleContentState(false, true, true)))
        {
            var week = new QuotaWindow(21, 10080, null);
            var snapshot = new QuotaSnapshot(DateTime.UtcNow, null, week);
            view.Present(new QuotaState(snapshot, null, false));
            Check(Field(view, "quotaPercent1") == null && (double)Field(view, "quotaPercent2") == 79, "Secondary-only weekly quota retains its remaining percentage");
            Check((string)Field(view, "line2") == "Week", "Weekly label comes from duration");
            view.Present(new QuotaState(snapshot, null, true));
            Check((string)Field(view, "status") == "Updating..." && (double)Field(view, "quotaPercent2") == 79, "Refresh preserves the weekly value");
            view.Present(new QuotaState(snapshot, "Offline", false));
            Check(((string)Field(view, "status")).StartsWith("Failed") && (double)Field(view, "quotaPercent2") == 79, "Failure retains last weekly quota");
            view.Present(new QuotaState(new QuotaSnapshot(DateTime.UtcNow, week, null), null, false));
            Check((string)Field(view, "line1") == "Week" && Field(view, "quotaPercent2") == null, "Primary-only weekly quota is supported");
            view.Present(new QuotaState(new QuotaSnapshot(DateTime.UtcNow, new QuotaWindow(16, 300, null), week), null, false));
            Check((double)Field(view, "quotaPercent1") == 84 && (double)Field(view, "quotaPercent2") == 79, "Two-window layout retains both values");
            view.Present(new QuotaState(null, "Unavailable", false));
            Check(Field(view, "quotaPercent1") == null && Field(view, "quotaPercent2") == null, "Missing snapshot clears old metrics instead of showing stale bars");
            using (var bitmap = new Bitmap(208,104))
            using (var g = Graphics.FromImage(bitmap))
            using (var detail = new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using (var amount = new Font("Segoe UI",16,FontStyle.Bold,GraphicsUnit.Pixel))
            {
                g.Clear(Color.White);
                typeof(QuotaLabel).GetMethod("DrawMetric",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,
                    new object[]{g,detail,amount,Brushes.Black,Brushes.Gray,"Week","79%",(double?)79,20,168});
                Check(bitmap.GetPixel(104,70).ToArgb() != Color.White.ToArgb(), "Single metric bar spans the former column gap");
            }
            Check(view.ClientSize == new Size(208,104), "Single quota keeps the shared bubble dimensions");
        }
        Console.WriteLine("9 single-window quota checks passed");
    }
}
