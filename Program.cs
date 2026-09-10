using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexQuotaMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 3 && string.Equals(args[0], "--render-icon", StringComparison.OrdinalIgnoreCase))
            {
                int iconValue;
                if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out iconValue))
                {
                    iconValue = 50;
                }
                RenderTrayIcon(Math.Max(0, Math.Min(100, iconValue)), args[2]);
                return;
            }

            if (args.Length >= 2 && string.Equals(args[0], "--diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                RunDiagnostics(args[1]);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool isFirstInstance;
            using (Mutex singleInstance = new Mutex(true, "Local\\CodexQuotaMonitor.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    return;
                }
                bool silentStart = HasArgument(args, "--silent");
                Application.Run(new MainForm(silentStart));
            }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RunDiagnostics(string outputPath)
        {
            CodexDataService service = new CodexDataService();
            Dictionary<string, object> report = new Dictionary<string, object>();

            try
            {
                QuotaSnapshot snapshot = service.FetchAsync().GetAwaiter().GetResult();
                report["ok"] = true;
                report["remainingPercent"] = snapshot.RemainingPercent;
                report["plan"] = snapshot.Plan;
                report["window"] = snapshot.WindowLabel;
                report["hasResetTime"] = snapshot.ResetsAt.HasValue;
            }
            catch (Exception ex)
            {
                report["ok"] = false;
                report["error"] = ex.Message;
            }
            finally
            {
                service.Dispose();
            }

            string fullPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(fullPath, serializer.Serialize(report));
        }

        private static void RenderTrayIcon(int value, string outputPath)
        {
            string fullPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (Icon icon = TrayIconFactory.Create(value))
            using (Bitmap bitmap = icon.ToBitmap())
            {
                bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Color _pageColor = Color.FromArgb(247, 249, 252);
        private readonly Color _cardColor = Color.White;
        private readonly Color _textColor = Color.FromArgb(24, 31, 44);
        private readonly Color _mutedColor = Color.FromArgb(103, 112, 127);
        private readonly Font _uiFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        private readonly Font _titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
        private readonly Font _smallBoldFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        private readonly QuotaRingControl _quotaRing;
        private readonly Label _accountLabel;
        private readonly Label _planLabel;
        private readonly Label _statusLabel;
        private readonly Label _windowLabel;
        private readonly Label _resetLabel;
        private readonly Label _lastUpdatedLabel;
        private readonly Button _refreshButton;
        private readonly CheckBox _trayToggle;
        private readonly Button _autoStartButton;
        private readonly Button _silentStartButton;
        private readonly TextBox _feishuWebhookBox;
        private readonly Label _feishuStatusLabel;
        private readonly Button _saveFeishuButton;
        private readonly Button _testFeishuButton;
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _menuOpen;
        private readonly ToolStripMenuItem _menuRefresh;
        private readonly ToolStripMenuItem _menuFeishu;
        private readonly ToolStripMenuItem _menuAutoStart;
        private readonly ToolStripMenuItem _menuSilentStart;
        private readonly System.Windows.Forms.Timer _refreshTimer;

        private CodexDataService _dataService;
        private Icon _dynamicTrayIcon;
        private bool _isRefreshing;
        private bool _allowExit;
        private bool _shownBackgroundNotice;
        private bool _updatingStartupSettings;
        private readonly bool _silentStart;

        public MainForm(bool silentStart)
        {
            _silentStart = silentStart;
            Text = "Codex Quota";
            Font = _uiFont;
            BackColor = _pageColor;
            ForeColor = _textColor;
            ClientSize = new Size(430, 760);
            MinimumSize = new Size(446, 799);
            MaximumSize = new Size(446, 799);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Opacity = _silentStart ? 0D : 1D;

            Panel header = new Panel();
            header.BackColor = _cardColor;
            header.Dock = DockStyle.Top;
            header.Height = 92;
            Controls.Add(header);

            Panel logo = new Panel();
            logo.BackColor = Color.FromArgb(31, 41, 55);
            logo.Location = new Point(24, 24);
            logo.Size = new Size(44, 44);
            logo.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(Color.White, 3F))
                {
                    e.Graphics.DrawArc(pen, 10, 10, 24, 24, -90, 275);
                }
            };
            header.Controls.Add(logo);

            Label title = new Label();
            title.Text = "Codex Quota";
            title.Font = _titleFont;
            title.AutoSize = true;
            title.Location = new Point(82, 18);
            header.Controls.Add(title);

            _accountLabel = new Label();
            _accountLabel.Text = "正在读取当前 Codex 账号…";
            _accountLabel.ForeColor = _mutedColor;
            _accountLabel.AutoEllipsis = true;
            _accountLabel.Location = new Point(84, 52);
            _accountLabel.Size = new Size(315, 24);
            header.Controls.Add(_accountLabel);

            RoundedPanel card = new RoundedPanel();
            card.BackColor = _cardColor;
            card.BorderColor = Color.FromArgb(226, 231, 239);
            card.Location = new Point(20, 112);
            card.Size = new Size(390, 355);
            Controls.Add(card);

            _quotaRing = new QuotaRingControl();
            _quotaRing.Location = new Point(80, 20);
            _quotaRing.Size = new Size(230, 230);
            _quotaRing.RemainingPercent = null;
            card.Controls.Add(_quotaRing);

            _statusLabel = new Label();
            _statusLabel.Text = "正在同步";
            _statusLabel.Font = _smallBoldFont;
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            _statusLabel.BackColor = Color.FromArgb(232, 236, 242);
            _statusLabel.ForeColor = _textColor;
            _statusLabel.Location = new Point(145, 243);
            _statusLabel.Size = new Size(100, 28);
            card.Controls.Add(_statusLabel);

            _windowLabel = new Label();
            _windowLabel.Text = "当前 Codex 剩余额度";
            _windowLabel.TextAlign = ContentAlignment.MiddleCenter;
            _windowLabel.Location = new Point(25, 278);
            _windowLabel.Size = new Size(340, 24);
            card.Controls.Add(_windowLabel);

            _resetLabel = new Label();
            _resetLabel.Text = "正在获取额度周期";
            _resetLabel.ForeColor = _mutedColor;
            _resetLabel.TextAlign = ContentAlignment.MiddleCenter;
            _resetLabel.Location = new Point(25, 306);
            _resetLabel.Size = new Size(340, 24);
            card.Controls.Add(_resetLabel);

            RoundedPanel settings = new RoundedPanel();
            settings.BackColor = _cardColor;
            settings.BorderColor = Color.FromArgb(226, 231, 239);
            settings.Location = new Point(20, 484);
            settings.Size = new Size(390, 220);
            Controls.Add(settings);

            _trayToggle = new CheckBox();
            _trayToggle.Text = "在任务栏通知区域显示";
            _trayToggle.Checked = true;
            _trayToggle.AutoSize = true;
            _trayToggle.Location = new Point(18, 12);
            _trayToggle.CheckedChanged += OnTrayToggleChanged;
            settings.Controls.Add(_trayToggle);

            _autoStartButton = CreateStartupButton(
                StartupSettings.IsEnabled() ? "开机自启：已开启" : "开机自启：未开启",
                new Point(18, 40),
                new Size(130, 34));
            _autoStartButton.Click += delegate
            {
                ApplyStartupSettings(!StartupSettings.IsEnabled(), StartupSettings.ReadSilentStart());
            };
            settings.Controls.Add(_autoStartButton);

            _silentStartButton = CreateStartupButton(
                StartupSettings.ReadSilentStart() ? "静默启动：已开启" : "静默启动：未开启",
                new Point(154, 40),
                new Size(128, 34));
            _silentStartButton.Click += delegate
            {
                ApplyStartupSettings(StartupSettings.IsEnabled(), !StartupSettings.ReadSilentStart());
            };
            settings.Controls.Add(_silentStartButton);

            _planLabel = new Label();
            _planLabel.Text = "每 1 分钟自动刷新";
            _planLabel.ForeColor = _mutedColor;
            _planLabel.AutoSize = true;
            _planLabel.Location = new Point(18, 76);
            settings.Controls.Add(_planLabel);

            _refreshButton = new Button();
            _refreshButton.Text = "立即刷新";
            _refreshButton.FlatStyle = FlatStyle.Flat;
            _refreshButton.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 219);
            _refreshButton.Location = new Point(285, 17);
            _refreshButton.Size = new Size(88, 34);
            _refreshButton.Click += delegate { RefreshQuotaAsync(); };
            settings.Controls.Add(_refreshButton);

            Label feishuTitle = new Label();
            feishuTitle.Text = "完成任务后通知飞书";
            feishuTitle.Font = _smallBoldFont;
            feishuTitle.AutoSize = true;
            feishuTitle.Location = new Point(18, 99);
            settings.Controls.Add(feishuTitle);

            _feishuWebhookBox = new TextBox();
            _feishuWebhookBox.Location = new Point(18, 120);
            _feishuWebhookBox.Size = new Size(354, 28);
            _feishuWebhookBox.Font = new Font("Consolas", 9F);
            _feishuWebhookBox.UseSystemPasswordChar = true;
            settings.Controls.Add(_feishuWebhookBox);

            _saveFeishuButton = new Button();
            _saveFeishuButton.Text = "保存并启用";
            _saveFeishuButton.FlatStyle = FlatStyle.Flat;
            _saveFeishuButton.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 219);
            _saveFeishuButton.Location = new Point(18, 156);
            _saveFeishuButton.Size = new Size(110, 34);
            _saveFeishuButton.Click += SaveFeishuSettings;
            settings.Controls.Add(_saveFeishuButton);

            _testFeishuButton = new Button();
            _testFeishuButton.Text = "发送测试";
            _testFeishuButton.FlatStyle = FlatStyle.Flat;
            _testFeishuButton.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 219);
            _testFeishuButton.Location = new Point(140, 156);
            _testFeishuButton.Size = new Size(95, 34);
            _testFeishuButton.Click += SendFeishuTestAsync;
            settings.Controls.Add(_testFeishuButton);

            _feishuStatusLabel = new Label();
            _feishuStatusLabel.Text = "可选：配置后，Codex 完成或失败时会发送通知。";
            _feishuStatusLabel.ForeColor = _mutedColor;
            _feishuStatusLabel.AutoEllipsis = true;
            _feishuStatusLabel.Location = new Point(18, 196);
            _feishuStatusLabel.Size = new Size(354, 24);
            settings.Controls.Add(_feishuStatusLabel);

            _lastUpdatedLabel = new Label();
            _lastUpdatedLabel.Text = "";
            _lastUpdatedLabel.ForeColor = _mutedColor;
            _lastUpdatedLabel.TextAlign = ContentAlignment.MiddleCenter;
            _lastUpdatedLabel.Location = new Point(20, 712);
            _lastUpdatedLabel.Size = new Size(390, 20);
            Controls.Add(_lastUpdatedLabel);

            string savedWebhook = FeishuNotifier.ReadWebhook();
            if (!string.IsNullOrEmpty(savedWebhook))
            {
                _feishuWebhookBox.Text = savedWebhook;
                _feishuStatusLabel.Text = "已检测到 Webhook；点击“保存并启用”即可更新 Codex 配置。";
            }

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Font = _uiFont;
            _menuOpen = new ToolStripMenuItem("打开 Codex Quota");
            _menuOpen.Click += delegate { ShowWindow(); };
            trayMenu.Items.Add(_menuOpen);
            _menuRefresh = new ToolStripMenuItem("立即刷新");
            _menuRefresh.Click += delegate { RefreshQuotaAsync(); };
            trayMenu.Items.Add(_menuRefresh);
            _menuAutoStart = new ToolStripMenuItem("开机自动启动");
            _menuAutoStart.Click += delegate
            {
                ApplyStartupSettings(!StartupSettings.IsEnabled(), StartupSettings.ReadSilentStart());
            };
            trayMenu.Items.Add(_menuAutoStart);
            _menuSilentStart = new ToolStripMenuItem("静默启动（仅托盘）");
            _menuSilentStart.Click += delegate
            {
                ApplyStartupSettings(StartupSettings.IsEnabled(), !StartupSettings.ReadSilentStart());
            };
            trayMenu.Items.Add(_menuSilentStart);
            _menuFeishu = new ToolStripMenuItem("飞书通知设置");
            _menuFeishu.Click += delegate { ShowFeishuSettings(); };
            trayMenu.Items.Add(_menuFeishu);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };
            trayMenu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "Codex Quota · 正在读取额度";
            _notifyIcon.ContextMenuStrip = trayMenu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate { ShowWindow(); };
            UpdateTrayIcon(null);
            UpdateStartupControls();

            _refreshTimer = new System.Windows.Forms.Timer();
            _refreshTimer.Interval = 60 * 1000;
            _refreshTimer.Tick += delegate { RefreshQuotaAsync(); };
            _refreshTimer.Start();

            _dataService = new CodexDataService();
            Shown += delegate
            {
                RefreshQuotaAsync();
                if (_silentStart)
                {
                    BeginInvoke(new Action(delegate
                    {
                        HideToTray(false);
                        Opacity = 1D;
                    }));
                }
            };
            FormClosing += OnFormClosing;
            Resize += OnResize;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                if (_dynamicTrayIcon != null)
                {
                    _dynamicTrayIcon.Dispose();
                    _dynamicTrayIcon = null;
                }
                if (_dataService != null)
                {
                    _dataService.Dispose();
                    _dataService = null;
                }
                _uiFont.Dispose();
                _titleFont.Dispose();
                _smallBoldFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private async void RefreshQuotaAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            _refreshButton.Enabled = false;
            _menuRefresh.Enabled = false;
            _refreshButton.Text = "刷新中…";

            try
            {
                QuotaSnapshot snapshot = await _dataService.FetchAsync();
                ApplySnapshot(snapshot);
            }
            catch (Exception ex)
            {
                ApplyError(ex.Message);
                _dataService.Dispose();
                _dataService = new CodexDataService();
            }
            finally
            {
                _refreshButton.Text = "立即刷新";
                _refreshButton.Enabled = true;
                _menuRefresh.Enabled = true;
                _isRefreshing = false;
            }
        }

        private void ApplySnapshot(QuotaSnapshot snapshot)
        {
            int remaining = snapshot.RemainingPercent;
            Color stateColor = GetStateColor(remaining);
            string status = GetStatusText(remaining);

            _quotaRing.RemainingPercent = remaining;
            _quotaRing.StateColor = stateColor;
            _accountLabel.Text = snapshot.Email;
            _statusLabel.Text = status;
            _statusLabel.BackColor = BlendWithWhite(stateColor, 0.84F);
            _statusLabel.ForeColor = Darken(stateColor, 0.30F);
            _windowLabel.Text = snapshot.WindowLabel + "剩余额度";
            _resetLabel.Text = snapshot.ResetsAt.HasValue
                ? "重置：" + snapshot.ResetsAt.Value.ToLocalTime().ToString("M月d日 HH:mm", CultureInfo.CurrentCulture)
                : "重置时间暂不可用";
            _planLabel.Text = FormatPlan(snapshot.Plan) + " · 每 1 分钟自动刷新";
            _lastUpdatedLabel.Text = "最后更新：" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

            UpdateTrayIcon(remaining);
            SetNotifyText("Codex 剩余额度 " + remaining.ToString(CultureInfo.InvariantCulture) + "% · " + snapshot.Email);
        }

        private void ApplyError(string message)
        {
            _quotaRing.RemainingPercent = null;
            _quotaRing.StateColor = Color.FromArgb(148, 163, 184);
            _statusLabel.Text = "暂不可用";
            _statusLabel.BackColor = Color.FromArgb(235, 238, 243);
            _statusLabel.ForeColor = _mutedColor;
            _windowLabel.Text = "无法读取当前 Codex 额度";
            _resetLabel.Text = ShortenError(message);
            _lastUpdatedLabel.Text = "刷新失败：" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            UpdateTrayIcon(null);
            SetNotifyText("Codex 额度暂不可用");
        }

        private void SaveFeishuSettings(object sender, EventArgs e)
        {
            string webhook = _feishuWebhookBox.Text.Trim();
            if (string.IsNullOrEmpty(webhook))
            {
                _feishuStatusLabel.Text = "请先粘贴飞书机器人的 Webhook。";
                return;
            }

            try
            {
                FeishuNotifier.SaveAndEnable(webhook);
                _feishuStatusLabel.Text = "已启用。请重启 Codex 后开始接收完成/失败通知。";
                _notifyIcon.ShowBalloonTip(2500, "飞书通知已启用", "Codex 完成任务后会发送到飞书。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _feishuStatusLabel.Text = "启用失败：" + ShortenError(ex.Message);
            }
        }

        private async void SendFeishuTestAsync(object sender, EventArgs e)
        {
            string webhook = _feishuWebhookBox.Text.Trim();
            if (string.IsNullOrEmpty(webhook))
            {
                _feishuStatusLabel.Text = "请先粘贴飞书机器人的 Webhook。";
                return;
            }

            _testFeishuButton.Enabled = false;
            _testFeishuButton.Text = "发送中…";
            try
            {
                string testText = "Codex 飞书通知测试\n时间：" +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
                await FeishuNotifier.SendTextAsync(webhook, testText);
                _feishuStatusLabel.Text = "测试消息已发送，请查看飞书群。";
                _notifyIcon.ShowBalloonTip(2500, "测试成功", "飞书已经收到测试消息。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _feishuStatusLabel.Text = "发送失败：" + ShortenError(ex.Message);
            }
            finally
            {
                _testFeishuButton.Text = "发送测试";
                _testFeishuButton.Enabled = true;
            }
        }

        private void UpdateTrayIcon(int? remaining)
        {
            Icon next = TrayIconFactory.Create(remaining);
            Icon previous = _dynamicTrayIcon;
            _dynamicTrayIcon = next;
            _notifyIcon.Icon = next;
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private void SetNotifyText(string text)
        {
            _notifyIcon.Text = text.Length <= 63 ? text : text.Substring(0, 63);
        }

        private void ShowWindow()
        {
            Opacity = 1D;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ShowFeishuSettings()
        {
            ShowWindow();
            _feishuWebhookBox.Focus();
        }

        private void ExitApplication()
        {
            _allowExit = true;
            Close();
        }

        private void OnTrayToggleChanged(object sender, EventArgs e)
        {
            _notifyIcon.Visible = _trayToggle.Checked;
        }

        private Button CreateStartupButton(string text, Point location, Size size)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 219);
            button.Location = location;
            button.Size = size;
            button.Font = _uiFont;
            return button;
        }

        private void ApplyStartupSettings(bool autoStart, bool silentStart)
        {
            if (_updatingStartupSettings)
            {
                return;
            }

            try
            {
                StartupSettings.Apply(autoStart, silentStart);
                UpdateStartupControls();
                string message = autoStart
                    ? (silentStart ? "已开启开机自启和静默启动。" : "已开启开机自启。")
                    : "已关闭开机自启。";
                _notifyIcon.ShowBalloonTip(2200, "启动设置已更新", message, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                UpdateStartupControls();
                _notifyIcon.ShowBalloonTip(3000, "启动设置保存失败", ShortenError(ex.Message), ToolTipIcon.Error);
            }
        }

        private void UpdateStartupControls()
        {
            _updatingStartupSettings = true;
            try
            {
                bool autoStart = StartupSettings.IsEnabled();
                bool silentStart = StartupSettings.ReadSilentStart();
                _autoStartButton.Text = autoStart ? "开机自启：已开启" : "开机自启：未开启";
                _silentStartButton.Text = silentStart ? "静默启动：已开启" : "静默启动：未开启";
                _menuAutoStart.Checked = autoStart;
                _menuSilentStart.Checked = silentStart;
            }
            finally
            {
                _updatingStartupSettings = false;
            }
        }

        private void OnResize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray(true);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(true);
            }
        }

        private void HideToTray(bool showNotice)
        {
            Hide();
            if (showNotice && !_shownBackgroundNotice && _notifyIcon.Visible)
            {
                _shownBackgroundNotice = true;
                _notifyIcon.BalloonTipTitle = "Codex Quota 仍在运行";
                _notifyIcon.BalloonTipText = "额度将在后台每分钟刷新一次。";
                _notifyIcon.ShowBalloonTip(2500);
            }
        }

        private static Color GetStateColor(int value)
        {
            if (value <= 10)
            {
                return Color.FromArgb(239, 68, 68);
            }
            if (value <= 30)
            {
                return Color.FromArgb(245, 158, 11);
            }
            return Color.FromArgb(16, 185, 129);
        }

        private static string GetStatusText(int value)
        {
            if (value <= 10)
            {
                return "额度不足";
            }
            if (value <= 30)
            {
                return "注意额度";
            }
            return "额度充足";
        }

        private static string FormatPlan(string plan)
        {
            if (string.IsNullOrEmpty(plan))
            {
                return "当前 Codex 账号";
            }

            return char.ToUpperInvariant(plan[0]) + plan.Substring(1) + " 账号";
        }

        private static string ShortenError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "请确认 Codex 已登录";
            }

            string clean = message.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= 48 ? clean : clean.Substring(0, 45) + "…";
        }

        private static Color BlendWithWhite(Color color, float whiteAmount)
        {
            int r = (int)(color.R * (1F - whiteAmount) + 255F * whiteAmount);
            int g = (int)(color.G * (1F - whiteAmount) + 255F * whiteAmount);
            int b = (int)(color.B * (1F - whiteAmount) + 255F * whiteAmount);
            return Color.FromArgb(r, g, b);
        }

        private static Color Darken(Color color, float amount)
        {
            return Color.FromArgb(
                (int)(color.R * (1F - amount)),
                (int)(color.G * (1F - amount)),
                (int)(color.B * (1F - amount)));
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public Color BorderColor { get; set; }

        public RoundedPanel()
        {
            DoubleBuffered = true;
            BorderColor = Color.LightGray;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundedPath(rect, 14))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), 14))
            {
                Region = new Region(path);
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class QuotaRingControl : Control
    {
        private int? _remainingPercent;
        private Color _stateColor = Color.FromArgb(148, 163, 184);

        public int? RemainingPercent
        {
            get { return _remainingPercent; }
            set
            {
                _remainingPercent = value;
                Invalidate();
            }
        }

        public Color StateColor
        {
            get { return _stateColor; }
            set
            {
                _stateColor = value;
                Invalidate();
            }
        }

        public QuotaRingControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(Width, Height) / 230F;
            float stroke = 14F * scale;
            RectangleF ring = new RectangleF(stroke, stroke, Width - stroke * 2F, Height - stroke * 2F);

            using (Pen track = new Pen(Color.FromArgb(229, 233, 240), stroke))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                e.Graphics.DrawEllipse(track, ring);
            }

            if (_remainingPercent.HasValue)
            {
                using (Pen progress = new Pen(_stateColor, stroke))
                {
                    progress.StartCap = LineCap.Round;
                    progress.EndCap = LineCap.Round;
                    float sweep = 360F * _remainingPercent.Value / 100F;
                    if (sweep > 0F)
                    {
                        e.Graphics.DrawArc(progress, ring, -90F, sweep);
                    }
                }
            }

            string value = _remainingPercent.HasValue
                ? _remainingPercent.Value.ToString(CultureInfo.InvariantCulture)
                : "--";

            using (Font numberFont = new Font("Segoe UI", 46F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font unitFont = new Font("Microsoft YaHei UI", 16F * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush textBrush = new SolidBrush(Color.FromArgb(24, 31, 44)))
            using (Brush mutedBrush = new SolidBrush(Color.FromArgb(103, 112, 127)))
            {
                SizeF numberSize = e.Graphics.MeasureString(value, numberFont);
                e.Graphics.DrawString(
                    value,
                    numberFont,
                    textBrush,
                    (Width - numberSize.Width) / 2F,
                    Height / 2F - numberSize.Height * 0.72F);

                string unit = _remainingPercent.HasValue ? "% 剩余" : "等待同步";
                SizeF unitSize = e.Graphics.MeasureString(unit, unitFont);
                e.Graphics.DrawString(
                    unit,
                    unitFont,
                    mutedBrush,
                    (Width - unitSize.Width) / 2F,
                    Height / 2F + numberSize.Height * 0.34F);
            }
        }
    }

    internal static class TrayIconFactory
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create(int? remainingPercent)
        {
            using (Bitmap bitmap = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);

                Color stateColor = remainingPercent.HasValue
                    ? GetStateColor(remainingPercent.Value)
                    : Color.FromArgb(148, 163, 184);

                RectangleF ring = new RectangleF(2.5F, 2.5F, 59F, 59F);
                using (Brush center = new SolidBrush(Color.FromArgb(31, 41, 55)))
                {
                    graphics.FillEllipse(center, 6F, 6F, 52F, 52F);
                }

                using (Pen track = new Pen(Color.FromArgb(176, 184, 196), 4.5F))
                using (Pen progress = new Pen(stateColor, 4.5F))
                {
                    track.StartCap = LineCap.Round;
                    track.EndCap = LineCap.Round;
                    progress.StartCap = LineCap.Round;
                    progress.EndCap = LineCap.Round;
                    graphics.DrawEllipse(track, ring);

                    if (remainingPercent.HasValue && remainingPercent.Value > 0)
                    {
                        graphics.DrawArc(progress, ring, -90F, 360F * remainingPercent.Value / 100F);
                    }
                }

                string number = remainingPercent.HasValue
                    ? remainingPercent.Value.ToString(CultureInfo.InvariantCulture)
                    : "--";
                float fontSize = number.Length >= 3 ? 28F : (number.Length == 2 ? 35F : 39F);
                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(Color.White))
                {
                    StringFormat format = StringFormat.GenericTypographic;
                    SizeF size = graphics.MeasureString(number, font, PointF.Empty, format);
                    graphics.DrawString(
                        number,
                        font,
                        brush,
                        (64F - size.Width) / 2F,
                        (64F - size.Height) / 2F - 1F,
                        format);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static Color GetStateColor(int value)
        {
            if (value <= 10)
            {
                return Color.FromArgb(239, 68, 68);
            }
            if (value <= 30)
            {
                return Color.FromArgb(245, 158, 11);
            }
            return Color.FromArgb(16, 185, 129);
        }
    }

    internal static class FeishuNotifier
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly string ConfigDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexQuotaMonitor");
        private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");
        private static readonly string HookPath = Path.Combine(ConfigDirectory, "codex-feishu-hook.ps1");
        private static readonly string LegacyConfigPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexFeishuNotifier", "config.json");

        public static string ReadWebhook()
        {
            string webhook = ReadWebhookFrom(ConfigPath);
            if (!string.IsNullOrEmpty(webhook))
            {
                return webhook;
            }
            return ReadWebhookFrom(LegacyConfigPath);
        }

        public static void SaveAndEnable(string webhook)
        {
            if (string.IsNullOrWhiteSpace(webhook))
            {
                throw new InvalidOperationException("Webhook 不能为空。");
            }

            Directory.CreateDirectory(ConfigDirectory);
            Dictionary<string, object> config = new Dictionary<string, object>();
            config["webhook"] = webhook.Trim();
            config["max_message_chars"] = 6000;
            File.WriteAllText(
                ConfigPath,
                Serializer.Serialize(config),
                new UTF8Encoding(false));

            string bundledHook = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-feishu-hook.ps1");
            if (!File.Exists(bundledHook))
            {
                throw new FileNotFoundException("找不到飞书通知组件，请重新解压完整软件包。", bundledHook);
            }
            File.Copy(bundledHook, HookPath, true);
            InstallCodexNotifyHook();
        }

        public static Task SendTextAsync(string webhook, string text)
        {
            return Task.Run(delegate
            {
                Dictionary<string, object> content = new Dictionary<string, object>();
                content["text"] = text;
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["msg_type"] = "text";
                payload["content"] = content;

                string responseText;
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    client.Encoding = Encoding.UTF8;
                    responseText = client.UploadString(webhook.Trim(), "POST", Serializer.Serialize(payload));
                }

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    return;
                }

                Dictionary<string, object> response = Serializer.DeserializeObject(responseText) as Dictionary<string, object>;
                if (response == null || !response.ContainsKey("code"))
                {
                    return;
                }

                int code = Convert.ToInt32(response["code"], CultureInfo.InvariantCulture);
                if (code != 0)
                {
                    string message = response.ContainsKey("msg")
                        ? Convert.ToString(response["msg"], CultureInfo.InvariantCulture)
                        : "飞书返回错误";
                    throw new InvalidOperationException(message);
                }
            });
        }

        private static string ReadWebhookFrom(string path)
        {
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                Dictionary<string, object> config =
                    Serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (config == null || !config.ContainsKey("webhook"))
                {
                    return "";
                }
                return Convert.ToString(config["webhook"], CultureInfo.InvariantCulture).Trim();
            }
            catch
            {
                return "";
            }
        }

        private static void InstallCodexNotifyHook()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string codexDirectory = Path.Combine(userProfile, ".codex");
            string codexConfig = Path.Combine(codexDirectory, "config.toml");
            if (!File.Exists(codexConfig))
            {
                throw new FileNotFoundException("找不到 Codex 配置文件，请先登录并运行一次 Codex。", codexConfig);
            }

            string content = File.ReadAllText(codexConfig, Encoding.UTF8);
            string backupPath = codexConfig + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Copy(codexConfig, backupPath, true);

            string cleaned = RemoveNotifySetting(content);
            string hookPathForToml = HookPath.Replace("\\", "/").Replace("\"", "\\\"");
            string notifyLine =
                "notify = [\"powershell.exe\", \"-NoProfile\", \"-ExecutionPolicy\", \"Bypass\", \"-File\", \"" +
                hookPathForToml + "\"]";
            string updated = notifyLine + Environment.NewLine + cleaned.TrimStart();
            File.WriteAllText(codexConfig, updated + Environment.NewLine, new UTF8Encoding(false));
        }

        private static string RemoveNotifySetting(string content)
        {
            string normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            List<string> kept = new List<string>();
            bool skipping = false;

            foreach (string line in lines)
            {
                if (!skipping && Regex.IsMatch(line, @"^\s*notify\s*=", RegexOptions.IgnoreCase))
                {
                    skipping = line.IndexOf("]", StringComparison.Ordinal) < 0;
                    continue;
                }
                if (skipping)
                {
                    if (line.IndexOf("]", StringComparison.Ordinal) >= 0)
                    {
                        skipping = false;
                    }
                    continue;
                }
                kept.Add(line);
            }

            return string.Join(Environment.NewLine, kept).TrimStart();
        }
    }

    internal static class StartupSettings
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunValueName = "CodexQuotaMonitor";
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly string ConfigDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexQuotaMonitor");
        private static readonly string SettingsPath = Path.Combine(ConfigDirectory, "settings.json");

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool ReadSilentStart()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return false;
                }

                Dictionary<string, object> settings =
                    Serializer.DeserializeObject(File.ReadAllText(SettingsPath, Encoding.UTF8)) as Dictionary<string, object>;
                object rawValue;
                if (settings != null && settings.TryGetValue("silentStart", out rawValue))
                {
                    return Convert.ToBoolean(rawValue, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return false;
        }

        public static void Apply(bool autoStart, bool silentStart)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开 Windows 开机启动设置。");
                }

                if (autoStart)
                {
                    string executablePath = Process.GetCurrentProcess().MainModule.FileName;
                    string command = "\"" + executablePath + "\"" + (silentStart ? " --silent" : "");
                    key.SetValue(RunValueName, command, RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }

            Directory.CreateDirectory(ConfigDirectory);
            Dictionary<string, object> settings = new Dictionary<string, object>();
            settings["silentStart"] = silentStart;
            File.WriteAllText(SettingsPath, Serializer.Serialize(settings), new UTF8Encoding(false));
        }
    }

    internal sealed class QuotaSnapshot
    {
        public string Email { get; set; }
        public string Plan { get; set; }
        public int RemainingPercent { get; set; }
        public DateTime? ResetsAt { get; set; }
        public string WindowLabel { get; set; }
    }

    internal sealed class CodexDataService : IDisposable
    {
        private readonly CodexAppServerClient _client = new CodexAppServerClient();

        public async Task<QuotaSnapshot> FetchAsync()
        {
            Dictionary<string, object> accountResult = await _client.RequestAsync(
                "account/read",
                new Dictionary<string, object> { { "refreshToken", false } });
            Dictionary<string, object> limitsResult = await _client.RequestAsync(
                "account/rateLimits/read",
                new Dictionary<string, object>());

            Dictionary<string, object> account = GetDictionary(accountResult, "account");
            if (account == null)
            {
                throw new InvalidOperationException("Codex 当前未登录 ChatGPT 账号");
            }

            Dictionary<string, object> bucket = SelectCodexBucket(limitsResult);
            if (bucket == null)
            {
                throw new InvalidOperationException("Codex 未返回可用的额度信息");
            }

            Dictionary<string, object> selectedWindow = SelectMostRestrictiveWindow(bucket);
            if (selectedWindow == null)
            {
                throw new InvalidOperationException("当前账号没有可显示的额度窗口");
            }

            int usedPercent = Clamp(ConvertToInt(GetValue(selectedWindow, "usedPercent")), 0, 100);
            long? resetSeconds = ConvertToNullableLong(GetValue(selectedWindow, "resetsAt"));
            long? durationMinutes = ConvertToNullableLong(GetValue(selectedWindow, "windowDurationMins"));

            QuotaSnapshot snapshot = new QuotaSnapshot();
            snapshot.Email = Convert.ToString(GetValue(account, "email"), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(snapshot.Email))
            {
                snapshot.Email = "当前 Codex 账号";
            }
            snapshot.Plan = Convert.ToString(GetValue(account, "planType"), CultureInfo.InvariantCulture);
            snapshot.RemainingPercent = 100 - usedPercent;
            snapshot.ResetsAt = resetSeconds.HasValue ? FromUnixTimeSeconds(resetSeconds.Value) : (DateTime?)null;
            snapshot.WindowLabel = FormatWindow(durationMinutes);
            return snapshot;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        private static Dictionary<string, object> SelectCodexBucket(Dictionary<string, object> result)
        {
            Dictionary<string, object> byLimitId = GetDictionary(result, "rateLimitsByLimitId");
            if (byLimitId != null)
            {
                object codexValue;
                if (byLimitId.TryGetValue("codex", out codexValue))
                {
                    Dictionary<string, object> codex = codexValue as Dictionary<string, object>;
                    if (codex != null)
                    {
                        return codex;
                    }
                }
            }

            return GetDictionary(result, "rateLimits");
        }

        private static Dictionary<string, object> SelectMostRestrictiveWindow(Dictionary<string, object> bucket)
        {
            Dictionary<string, object> primary = GetDictionary(bucket, "primary");
            Dictionary<string, object> secondary = GetDictionary(bucket, "secondary");
            if (primary == null)
            {
                return secondary;
            }
            if (secondary == null)
            {
                return primary;
            }

            int primaryUsed = ConvertToInt(GetValue(primary, "usedPercent"));
            int secondaryUsed = ConvertToInt(GetValue(secondary, "usedPercent"));
            return primaryUsed >= secondaryUsed ? primary : secondary;
        }

        private static string FormatWindow(long? minutes)
        {
            if (!minutes.HasValue || minutes.Value <= 0)
            {
                return "当前周期";
            }
            if (minutes.Value % 10080 == 0)
            {
                long weeks = minutes.Value / 10080;
                return weeks == 1 ? "每周" : weeks.ToString(CultureInfo.InvariantCulture) + " 周";
            }
            if (minutes.Value % 1440 == 0)
            {
                return (minutes.Value / 1440).ToString(CultureInfo.InvariantCulture) + " 天";
            }
            if (minutes.Value % 60 == 0)
            {
                return (minutes.Value / 60).ToString(CultureInfo.InvariantCulture) + " 小时";
            }
            return minutes.Value.ToString(CultureInfo.InvariantCulture) + " 分钟";
        }

        private static DateTime FromUnixTimeSeconds(long seconds)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(seconds);
        }

        private static object GetValue(Dictionary<string, object> dictionary, string key)
        {
            if (dictionary == null)
            {
                return null;
            }
            object value;
            return dictionary.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary, string key)
        {
            return GetValue(dictionary, key) as Dictionary<string, object>;
        }

        private static int ConvertToInt(object value)
        {
            if (value == null)
            {
                return 0;
            }
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static long? ConvertToNullableLong(object value)
        {
            if (value == null)
            {
                return null;
            }
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }

    internal sealed class CodexAppServerClient : IDisposable
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly object _pendingLock = new object();
        private readonly object _writeLock = new object();
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);

        private Process _process;
        private int _nextRequestId;
        private bool _initialized;
        private bool _disposed;
        private string _lastError;

        public async Task<Dictionary<string, object>> RequestAsync(string method, Dictionary<string, object> parameters)
        {
            await EnsureStartedAsync();
            return await SendRequestAsync(method, parameters, 15000);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            StopProcess();
            _startLock.Dispose();
        }

        private async Task EnsureStartedAsync()
        {
            if (_initialized && _process != null && !_process.HasExited)
            {
                return;
            }

            await _startLock.WaitAsync();
            try
            {
                if (_initialized && _process != null && !_process.HasExited)
                {
                    return;
                }

                StopProcess();
                StartProcess();

                Dictionary<string, object> clientInfo = new Dictionary<string, object>();
                clientInfo["name"] = "codex-quota-monitor";
                clientInfo["title"] = "Codex Quota Monitor";
                clientInfo["version"] = "1.0.0";

                Dictionary<string, object> initializeParams = new Dictionary<string, object>();
                initializeParams["clientInfo"] = clientInfo;
                initializeParams["capabilities"] = null;

                await SendRequestAsync("initialize", initializeParams, 15000);
                SendNotification("initialized");
                _initialized = true;
            }
            finally
            {
                _startLock.Release();
            }
        }

        private void StartProcess()
        {
            string codexPath = CodexExecutableResolver.Find();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = codexPath;
            startInfo.Arguments = "app-server --stdio";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

            _process = new Process();
            _process.StartInfo = startInfo;
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.Exited += OnProcessExited;

            if (!_process.Start())
            {
                throw new InvalidOperationException("无法启动 Codex app-server");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private async Task<Dictionary<string, object>> SendRequestAsync(
            string method,
            Dictionary<string, object> parameters,
            int timeoutMilliseconds)
        {
            if (_process == null || _process.HasExited)
            {
                throw new InvalidOperationException("Codex app-server 未运行");
            }

            int requestId = Interlocked.Increment(ref _nextRequestId);
            TaskCompletionSource<Dictionary<string, object>> completion =
                new TaskCompletionSource<Dictionary<string, object>>();
            lock (_pendingLock)
            {
                _pending[requestId] = completion;
            }

            Dictionary<string, object> request = new Dictionary<string, object>();
            request["id"] = requestId;
            request["method"] = method;
            request["params"] = parameters;
            WriteMessage(request);

            Task winner = await Task.WhenAny(completion.Task, Task.Delay(timeoutMilliseconds));
            if (winner != completion.Task)
            {
                lock (_pendingLock)
                {
                    _pending.Remove(requestId);
                }
                throw new TimeoutException("读取 Codex 额度超时");
            }

            return await completion.Task;
        }

        private void SendNotification(string method)
        {
            Dictionary<string, object> notification = new Dictionary<string, object>();
            notification["method"] = method;
            WriteMessage(notification);
        }

        private void WriteMessage(Dictionary<string, object> message)
        {
            string line = _serializer.Serialize(message);
            lock (_writeLock)
            {
                if (_process == null || _process.HasExited)
                {
                    throw new InvalidOperationException("Codex app-server 已停止");
                }
                _process.StandardInput.WriteLine(line);
                _process.StandardInput.Flush();
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            try
            {
                object parsed = _serializer.DeserializeObject(e.Data);
                Dictionary<string, object> message = parsed as Dictionary<string, object>;
                if (message == null)
                {
                    return;
                }

                object idValue;
                if (!message.TryGetValue("id", out idValue) || idValue == null)
                {
                    return;
                }

                int requestId = Convert.ToInt32(idValue, CultureInfo.InvariantCulture);
                TaskCompletionSource<Dictionary<string, object>> completion = null;
                lock (_pendingLock)
                {
                    if (_pending.TryGetValue(requestId, out completion))
                    {
                        _pending.Remove(requestId);
                    }
                }

                if (completion == null)
                {
                    return;
                }

                object errorValue;
                if (message.TryGetValue("error", out errorValue) && errorValue != null)
                {
                    Dictionary<string, object> error = errorValue as Dictionary<string, object>;
                    string errorMessage = error != null && error.ContainsKey("message")
                        ? Convert.ToString(error["message"], CultureInfo.InvariantCulture)
                        : "Codex 返回了未知错误";
                    completion.TrySetException(new InvalidOperationException(errorMessage));
                    return;
                }

                object resultValue;
                Dictionary<string, object> result =
                    message.TryGetValue("result", out resultValue)
                        ? resultValue as Dictionary<string, object>
                        : null;
                completion.TrySetResult(result ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _lastError = e.Data.Trim();
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            _initialized = false;
            string error = string.IsNullOrEmpty(_lastError)
                ? "Codex app-server 已退出"
                : "Codex app-server 已退出：" + _lastError;
            FailPending(new InvalidOperationException(error));
        }

        private void FailPending(Exception exception)
        {
            List<TaskCompletionSource<Dictionary<string, object>>> completions;
            lock (_pendingLock)
            {
                completions = new List<TaskCompletionSource<Dictionary<string, object>>>(_pending.Values);
                _pending.Clear();
            }
            foreach (TaskCompletionSource<Dictionary<string, object>> completion in completions)
            {
                completion.TrySetException(exception);
            }
        }

        private void StopProcess()
        {
            _initialized = false;
            Process process = _process;
            _process = null;
            if (process == null)
            {
                return;
            }

            try
            {
                process.OutputDataReceived -= OnOutputDataReceived;
                process.ErrorDataReceived -= OnErrorDataReceived;
                process.Exited -= OnProcessExited;
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }

            FailPending(new ObjectDisposedException("Codex app-server"));
        }
    }

    internal static class CodexExecutableResolver
    {
        public static string Find()
        {
            string overridePath = Environment.GetEnvironmentVariable("CODEX_QUOTA_CODEX_PATH");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string npmPath = Path.Combine(
                appData,
                "npm",
                "node_modules",
                "@openai",
                "codex",
                "node_modules",
                "@openai",
                "codex-win32-x64",
                "vendor",
                "x86_64-pc-windows-msvc",
                "bin",
                "codex.exe");
            if (File.Exists(npmPath))
            {
                return npmPath;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string bundledBinRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            string bundledPath = FindNewestCodexBinary(bundledBinRoot);
            if (!string.IsNullOrEmpty(bundledPath))
            {
                return bundledPath;
            }

            string fromPath = FindWithWhere();
            if (!string.IsNullOrEmpty(fromPath))
            {
                return fromPath;
            }

            throw new FileNotFoundException("没有找到 codex.exe。请先安装并登录 Codex。");
        }

        private static string FindNewestCodexBinary(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return null;
            }

            try
            {
                string newestPath = null;
                DateTime newestWriteTime = DateTime.MinValue;
                string[] candidates = Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories);
                foreach (string candidate in candidates)
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    DateTime writeTime = File.GetLastWriteTimeUtc(candidate);
                    if (newestPath == null || writeTime > newestWriteTime)
                    {
                        newestPath = candidate;
                        newestWriteTime = writeTime;
                    }
                }

                return newestPath;
            }
            catch
            {
                return null;
            }
        }

        private static string FindWithWhere()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "where.exe";
                startInfo.Arguments = "codex.exe";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string candidate = line.Trim();
                        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
