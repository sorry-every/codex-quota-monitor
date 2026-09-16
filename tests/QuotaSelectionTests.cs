using System;
using System.Collections.Generic;
using CodexQuotaMonitor;

internal static class QuotaSelectionTests
{
    private static Dictionary<string, object> Window(int remaining, long duration)
    {
        return new Dictionary<string, object> {
            { "usedPercent", 100 - remaining }, { "windowDurationMins", duration }, { "resetsAt", 1800000000L }
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [STAThread]
    public static void Main()
    {
        foreach (int[] remaining in new[] { new[] { 80, 40 }, new[] { 30, 90 } })
        {
            var five = Window(remaining[0], 300);
            var week = Window(remaining[1], 10080);
            foreach (bool reversed in new[] { false, true })
            {
                var bucket = new Dictionary<string, object> {
                    { "primary", reversed ? week : five }, { "secondary", reversed ? five : week }
                };
                var snapshot = new QuotaSnapshot { Windows = bucket, Email = "test", Plan = "plus" };
                Check(CodexDataService.SelectSnapshot(snapshot, "auto").RemainingPercent == Math.Min(remaining[0], remaining[1]), "Automatic selection");
                Check(CodexDataService.SelectSnapshot(snapshot, "fiveHour").RemainingPercent == remaining[0], "Fixed five-hour selection");
                Check(CodexDataService.SelectSnapshot(snapshot, "weekly").RemainingPercent == remaining[1], "Fixed weekly selection");
                Check(CodexDataService.SelectSnapshot(snapshot, "weekly").WindowLabel == "每周", "Weekly label");
                Check(CodexDataService.SelectSnapshot(snapshot, "fiveHour").ResetsAt.HasValue, "Reset time");
            }
        }
        var partial = new Dictionary<string, object> { { "primary", Window(80, 300) } };
        Check(CodexDataService.SelectWindow(partial, "weekly") == null, "Missing weekly must not fall back");
        Check(CodexDataService.SelectWindow(partial, "auto") != null, "Automatic single window");
        partial["primary"] = new Dictionary<string, object> { { "windowDurationMins", 300L } };
        Check(CodexDataService.SelectWindow(partial, "fiveHour") == null, "Missing usage must not show 100%");
        partial["primary"] = Window(50, 1440);
        Check(CodexDataService.SelectWindow(partial, "fiveHour") == null, "Unknown duration must not be mislabeled");
        Console.WriteLine("Quota selection regression checks passed.");
        System.Windows.Forms.Application.EnableVisualStyles();
        using (var form = new MainForm(false))
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var selector = (System.Windows.Forms.ComboBox)typeof(MainForm).GetField("_quotaModeBox", flags).GetValue(form);
            Check(selector.Items.Count == 3 && selector.SelectedIndex >= 0, "Selector initialized");
            Check(selector.Bottom <= selector.Parent.ClientSize.Height, "Selector within settings card");
            foreach (System.Windows.Forms.Control control in selector.Parent.Controls)
                if (control != selector) Check(!control.Bounds.IntersectsWith(selector.Bounds), "Selector overlaps " + control.Text);
            var bucket = new Dictionary<string, object> { { "primary", Window(80, 300) }, { "secondary", Window(40, 10080) } };
            var sample = new QuotaSnapshot { Windows = bucket, Email = "demo@example.com", Plan = "plus" };
            typeof(MainForm).GetMethod("ApplySnapshot", flags).Invoke(form, new object[] { CodexDataService.SelectSnapshot(sample, "weekly") });
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                typeof(System.Windows.Forms.Control).GetMethod("CreateControl", flags, null, new[] { typeof(bool) }, null)
                    .Invoke(form, new object[] { true });
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                System.IO.Directory.CreateDirectory("release");
                bitmap.Save("release/quota-layout.png", System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        Console.WriteLine("Selector layout checks passed; release/quota-layout.png rendered.");
    }
}
