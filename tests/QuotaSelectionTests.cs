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
            var refresh = (System.Windows.Forms.Button)typeof(MainForm).GetField("_refreshButton", flags).GetValue(form);
            var autoStart = (System.Windows.Forms.Button)typeof(MainForm).GetField("_autoStartButton", flags).GetValue(form);
            var silent = (System.Windows.Forms.Button)typeof(MainForm).GetField("_silentStartButton", flags).GetValue(form);
            Check(refresh.Top == autoStart.Top && refresh.Top == silent.Top, "Action buttons share a baseline");
            Check(refresh.Height == autoStart.Height && refresh.Height == silent.Height, "Action buttons share a height");
            Check(!refresh.Bounds.IntersectsWith(silent.Bounds), "Refresh does not overlap silent button");
            var interval = (System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("_refreshSecondsBox", flags).GetValue(form);
            Check(interval.Minimum == 10 && interval.Maximum == 3600, "Custom interval bounds");
            var timer = (System.Windows.Forms.Timer)typeof(MainForm).GetField("_refreshTimer", flags).GetValue(form);
            Check(timer.Interval == (int)interval.Value * 1000, "Timer uses saved interval");
            foreach (System.Windows.Forms.Control control in interval.Parent.Controls)
                if (control != interval) Check(!control.Bounds.IntersectsWith(interval.Bounds), "Interval overlaps " + control.Text);
            Check(interval.Bottom <= interval.Parent.ClientSize.Height, "Interval within card");
            System.IO.Directory.CreateDirectory("release");
            var pathField = typeof(StartupSettings).GetField("SettingsPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var originalPath = pathField.GetValue(null);
            try
            {
                pathField.SetValue(null, System.IO.Path.GetFullPath("release/test-settings-" + Guid.NewGuid().ToString("N") + ".json"));
                StartupSettings.SaveQuotaMode("weekly");
                typeof(MainForm).GetMethod("ChangeRefreshInterval", flags).Invoke(form, new object[] { 120 });
                Check(timer.Interval == 120000 && timer.Enabled, "Interval change restarts timer");
                Check(interval.Value == 120 && StartupSettings.ReadRefreshSeconds() == 120, "Interval control and persistence");
                Check(StartupSettings.ReadQuotaMode() == "weekly", "Interval preserves quota selection");
                StartupSettings.SaveQuotaMode("fiveHour");
                Check(StartupSettings.ReadRefreshSeconds() == 120, "Quota selection preserves interval");
            }
            finally { pathField.SetValue(null, originalPath); }
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
