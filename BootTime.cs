using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BootTimeTool
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            try { SetProcessDPIAware(); }
            catch { }

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    MainForm.UiScale = g.DpiX / 96f;
                }
            }
            catch
            {
                MainForm.UiScale = 1f;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        [DllImport("kernel32.dll")]
        private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        internal static float UiScale = 1f;

        private static int S(int value)
        {
            return (int)Math.Round(value * UiScale);
        }

        private static readonly Color Bg = Color.FromArgb(22, 22, 26);
        private static readonly Color Card = Color.FromArgb(33, 33, 39);
        private static readonly Color TextMain = Color.FromArgb(240, 240, 245);
        private static readonly Color TextDim = Color.FromArgb(138, 138, 152);
        private static readonly Color Divider = Color.FromArgb(46, 46, 54);
        private static readonly Color Accent = Color.FromArgb(90, 209, 160);
        private static readonly Color Warn = Color.FromArgb(255, 122, 122);

        private DateTime bootTime;
        private bool fromWmi;

        private Label bootValue;
        private Label bootCaption;
        private Label nowLabel;
        private Label uptimeValue;
        private Label splitValue;
        private Label infoValue;
        private TableLayoutPanel historyTable;
        private Timer ticker;

        public MainForm()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BuildUi();
            RefreshBoot();
            UpdateClocks();
            LoadHistory();

            ticker = new Timer();
            ticker.Interval = 1000;
            ticker.Tick += delegate { UpdateClocks(); };
            ticker.Start();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = "开机时间";
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(S(440), S(596));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Bg;
            root.ColumnCount = 1;
            root.RowCount = 10;
            root.Padding = new Padding(S(28), S(22), S(28), S(18));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            bootCaption = Caption("本次开机时间");
            bootCaption.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            bootCaption.Margin = new Padding(0, 0, 0, S(2));

            nowLabel = new Label();
            nowLabel.AutoSize = true;
            nowLabel.ForeColor = TextDim;
            nowLabel.Font = new Font("Microsoft YaHei UI", 9F);
            nowLabel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            nowLabel.Margin = new Padding(0, 0, 0, S(2));
            nowLabel.Text = "现在 —";

            TableLayoutPanel header = new TableLayoutPanel();
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.AutoSize = true;
            header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            header.Dock = DockStyle.Fill;
            header.BackColor = Bg;
            header.Margin = new Padding(0);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(bootCaption, 0, 0);
            header.Controls.Add(nowLabel, 1, 0);

            bootValue = new Label();
            bootValue.AutoSize = true;
            bootValue.ForeColor = TextMain;
            bootValue.Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold);
            bootValue.Margin = new Padding(0, 0, 0, S(16));
            bootValue.Text = "—";

            uptimeValue = new Label();
            uptimeValue.AutoSize = true;
            uptimeValue.ForeColor = Accent;
            uptimeValue.Font = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold);
            uptimeValue.Margin = new Padding(0, 0, 0, S(2));
            uptimeValue.Text = "—";

            splitValue = new Label();
            splitValue.AutoSize = true;
            splitValue.ForeColor = TextDim;
            splitValue.Font = new Font("Microsoft YaHei UI", 8.5F);
            splitValue.Margin = new Padding(0, 0, 0, 0);
            splitValue.MaximumSize = new Size(S(384), 0);
            splitValue.Text = "";

            infoValue = new Label();
            infoValue.AutoSize = true;
            infoValue.ForeColor = TextDim;
            infoValue.Font = new Font("Microsoft YaHei UI", 8.5F);
            infoValue.Margin = new Padding(0, S(8), 0, 0);
            infoValue.MaximumSize = new Size(S(384), 0);
            infoValue.Text = "";

            Panel divider = new Panel();
            divider.Height = S(1);
            divider.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            divider.BackColor = Divider;
            divider.Margin = new Padding(0, S(14), 0, S(10));

            Label historyCaption = Caption("最近开关机记录");
            historyCaption.Margin = new Padding(0, 0, 0, S(6));

            Panel historyScroll = new Panel();
            historyScroll.Dock = DockStyle.Fill;
            historyScroll.AutoScroll = true;
            historyScroll.BackColor = Bg;
            historyScroll.Margin = new Padding(0, 0, 0, S(10));

            historyTable = new TableLayoutPanel();
            historyTable.Dock = DockStyle.Top;
            historyTable.AutoSize = true;
            historyTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            historyTable.ColumnCount = 3;
            historyTable.RowCount = 0;
            historyTable.BackColor = Bg;
            historyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(104)));
            historyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(96)));
            historyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            historyScroll.Controls.Add(historyTable);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0, S(6), 0, 0);

            Button closeBtn = MakeButton("关闭", false);
            closeBtn.Click += delegate { Close(); };

            Button refreshBtn = MakeButton("刷新", false);
            refreshBtn.Click += delegate
            {
                RefreshBoot();
                UpdateClocks();
                LoadHistory();
            };

            Button copyBtn = MakeButton("复制", true);
            copyBtn.Click += delegate { CopySummary(); };

            buttons.Controls.Add(closeBtn);
            buttons.Controls.Add(refreshBtn);
            buttons.Controls.Add(copyBtn);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(bootValue, 0, 1);
            root.Controls.Add(Caption("已运行时长"), 0, 2);
            root.Controls.Add(uptimeValue, 0, 3);
            root.Controls.Add(splitValue, 0, 4);
            root.Controls.Add(infoValue, 0, 5);
            root.Controls.Add(divider, 0, 6);
            root.Controls.Add(historyCaption, 0, 7);
            root.Controls.Add(historyScroll, 0, 8);
            root.Controls.Add(buttons, 0, 9);

            Controls.Add(root);

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                if (e.Control && e.KeyCode == Keys.C) CopySummary();
            };

            ResumeLayout();
        }

        private static Label Caption(string text)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.ForeColor = TextDim;
            label.Font = new Font("Microsoft YaHei UI", 9F);
            label.Margin = new Padding(0, 0, 0, 1);
            return label;
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(S(16), S(7), S(16), S(7));
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Microsoft YaHei UI", 9F);
            button.Margin = new Padding(S(8), 0, 0, 0);
            button.Cursor = Cursors.Hand;
            button.TabStop = false;

            if (primary)
            {
                button.BackColor = Accent;
                button.ForeColor = Color.FromArgb(12, 36, 26);
                button.FlatAppearance.BorderColor = Accent;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(116, 226, 178);
            }
            else
            {
                button.BackColor = Card;
                button.ForeColor = TextMain;
                button.FlatAppearance.BorderColor = Color.FromArgb(58, 58, 68);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 48, 57);
            }

            return button;
        }

        private void RefreshBoot()
        {
            fromWmi = false;

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        object raw = mo["LastBootUpTime"];
                        if (raw != null)
                        {
                            bootTime = ManagementDateTimeConverter.ToDateTime(raw.ToString());
                            fromWmi = true;
                        }
                    }
                }
            }
            catch
            {
                fromWmi = false;
            }

            if (!fromWmi)
            {
                // 退路：直接用系统开机以来的毫秒数推算
                bootTime = DateTime.Now - TimeSpan.FromMilliseconds((double)GetTickCount64());
            }

            if (bootTime > DateTime.Now) bootTime = DateTime.Now;

            bootValue.Text = bootTime.ToString("yyyy-MM-dd  HH:mm:ss", CultureInfo.CurrentCulture);
            bootCaption.Text = "本次开机时间 · 周" + "日一二三四五六"[(int)bootTime.DayOfWeek];

            infoValue.Text = DescribeSystem()
                + (fromWmi ? "" : "\n（未取到系统开机记录，已按系统计时推算）");
        }

        /// <summary>不含睡眠/休眠的真实运行时间；失败时退回系统计时器。</summary>
        private static TimeSpan RunningTime()
        {
            ulong unbiased;
            try
            {
                if (QueryUnbiasedInterruptTime(out unbiased))
                    return TimeSpan.FromTicks((long)unbiased);
            }
            catch { }

            return TimeSpan.FromMilliseconds((double)GetTickCount64());
        }

        private void UpdateClocks()
        {
            DateTime now = DateTime.Now;

            TimeSpan sinceBoot = now - bootTime;
            if (sinceBoot < TimeSpan.Zero) sinceBoot = TimeSpan.Zero;

            TimeSpan running = RunningTime();
            if (running < TimeSpan.Zero) running = TimeSpan.Zero;
            if (running > sinceBoot) running = sinceBoot;

            TimeSpan idle = sinceBoot - running;

            string runningText = FormatSpan(running, true);
            uptimeValue.Text = runningText;
            splitValue.Text = "开机至今 " + FormatSpan(sinceBoot, true)
                + "　·　睡眠 / 休眠 " + FormatSpan(idle, true);

            nowLabel.Text = "现在 " + now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            Text = "开机时间 · 已运行 " + runningText;
        }

        private static string FormatSpan(TimeSpan span, bool withSeconds)
        {
            StringBuilder sb = new StringBuilder();
            if (span.Days > 0) sb.Append(span.Days.ToString(CultureInfo.CurrentCulture)).Append(" 天 ");

            if (withSeconds)
            {
                sb.AppendFormat("{0:00}:{1:00}:{2:00}", span.Hours, span.Minutes, span.Seconds);
            }
            else if (span.TotalMinutes < 1)
            {
                sb.Append("不足 1 分钟");
            }
            else if (span.Hours > 0)
            {
                sb.AppendFormat("{0} 小时 {1} 分", span.Hours, span.Minutes);
            }
            else
            {
                sb.AppendFormat("{0} 分钟", span.Minutes);
            }

            return sb.ToString();
        }

        private sealed class BootEvent
        {
            public DateTime Time;
            public int Id;
            public string Kind;
            public string Note;
        }

        private void LoadHistory()
        {
            historyTable.SuspendLayout();
            historyTable.Controls.Clear();
            historyTable.RowStyles.Clear();
            historyTable.RowCount = 0;

            List<string[]> rows = null;
            string error = null;

            try
            {
                rows = BuildHistoryRows();
            }
            catch (Exception ex)
            {
                error = "读取系统事件日志失败：" + ex.Message;
            }

            if (error != null)
            {
                historyTable.RowCount = 1;
                historyTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Label label = new Label();
                label.AutoSize = true;
                label.MaximumSize = new Size(S(384), 0);
                label.ForeColor = Warn;
                label.Font = new Font("Microsoft YaHei UI", 8.5F);
                label.Text = error + "\n（可以尝试以管理员身份运行本程序）";
                label.Margin = new Padding(0, S(2), 0, 0);
                historyTable.Controls.Add(label, 0, 0);
                historyTable.SetColumnSpan(label, 3);
                historyTable.ResumeLayout();
                return;
            }

            foreach (string[] row in rows)
            {
                int r = historyTable.RowCount;
                historyTable.RowCount = r + 1;
                historyTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                Color color = TextDim;
                if (row[1] == "启动") color = Accent;
                else if (row[1].StartsWith("异常")) color = Warn;

                historyTable.Controls.Add(HistoryCell(row[0], color), 0, r);
                historyTable.Controls.Add(HistoryCell(row[1], color), 1, r);
                historyTable.Controls.Add(HistoryCell(row[2], TextDim), 2, r);
            }

            historyTable.ResumeLayout();
        }

        private static Label HistoryCell(string text, Color color)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.ForeColor = color;
            label.Font = new Font("Microsoft YaHei UI", 9F);
            label.Margin = new Padding(0, S(2), S(6), S(2));
            label.AutoEllipsis = true;
            return label;
        }

        private List<string[]> BuildHistoryRows()
        {
            List<BootEvent> events = new List<BootEvent>();

            string xpath = "*[System[(EventID=6005 or EventID=6006 or EventID=6008 or EventID=41 or EventID=1074)]]";
            EventLogQuery query = new EventLogQuery("System", PathType.LogName, xpath);

            using (EventLogReader reader = new EventLogReader(query))
            {
                for (int i = 0; i < 400; i++)
                {
                    EventRecord record = reader.ReadEvent();
                    if (record == null) break;

                    using (record)
                    {
                        int id = record.Id;
                        string provider = record.ProviderName ?? "";

                        // 同一个事件 ID 在别的提供程序下含义不同，过滤掉噪声
                        bool relevant =
                            ((id == 6005 || id == 6006 || id == 6008) && provider == "EventLog") ||
                            (id == 41 && provider == "Microsoft-Windows-Kernel-Power") ||
                            (id == 1074 && provider == "User32");

                        if (relevant && record.TimeCreated.HasValue)
                        {
                            events.Add(new BootEvent { Time = record.TimeCreated.Value, Id = id });
                        }
                    }
                }
            }

            events.Sort(delegate(BootEvent a, BootEvent b) { return a.Time.CompareTo(b.Time); });

            // 标记类型，并去掉 1074 与随后 6006 的重复记录（同一次关机）
            List<BootEvent> cleaned = new List<BootEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                BootEvent e = events[i];

                if (e.Id == 1074 && i + 1 < events.Count &&
                    events[i + 1].Id == 6006 &&
                    (events[i + 1].Time - e.Time).TotalSeconds < 150)
                {
                    continue;
                }

                if (e.Id == 6005) { e.Kind = "启动"; e.Note = ""; }
                else if (e.Id == 6006) { e.Kind = "正常关机"; e.Note = ""; }
                else if (e.Id == 1074) { e.Kind = "关机 / 重启"; e.Note = ""; }
                else { e.Kind = "异常关机"; e.Note = "上次系统非正常关闭"; }

                // 41 和 6008 往往在同一次启动时各记一条，合并掉
                if (e.Kind == "异常关机")
                {
                    bool duplicate = false;
                    foreach (BootEvent seen in cleaned)
                    {
                        if (seen.Kind == "异常关机" &&
                            Math.Abs((e.Time - seen.Time).TotalSeconds) < 120)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (duplicate) continue;
                }

                cleaned.Add(e);
            }

            // 给关机记录补上「本次运行了多久」：找它之前最近的一次启动
            DateTime? lastBoot = null;
            foreach (BootEvent e in cleaned)
            {
                if (e.Kind == "启动")
                {
                    lastBoot = e.Time;
                }
                else if (e.Kind != "异常关机" && lastBoot.HasValue)
                {
                    TimeSpan span = e.Time - lastBoot.Value;
                    if (span > TimeSpan.Zero) e.Note = "运行 " + FormatSpan(span, false);
                    lastBoot = null;
                }
            }

            List<string[]> rows = new List<string[]>();
            for (int i = cleaned.Count - 1; i >= 0 && rows.Count < 12; i--)
            {
                BootEvent e = cleaned[i];
                rows.Add(new string[]
                {
                    e.Time.ToString("MM-dd HH:mm", CultureInfo.CurrentCulture),
                    e.Kind,
                    e.Note
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new string[] { "—", "暂无记录", "系统未提供开关机事件" });
            }

            return rows;
        }

        private void CopySummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("本次开机时间：" + bootValue.Text);
            sb.AppendLine("已运行时长：" + uptimeValue.Text + "（不含睡眠 / 休眠）");
            sb.AppendLine(splitValue.Text.Replace("\u3000", " "));
            sb.AppendLine("系统：" + DescribeSystem());
            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }

        private static string DescribeSystem()
        {
            string name = "Windows";
            string build = "";

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object product = key.GetValue("ProductName");
                        object currentBuild = key.GetValue("CurrentBuild");
                        object displayVersion = key.GetValue("DisplayVersion");

                        if (product != null) name = product.ToString();
                        if (currentBuild != null) build = currentBuild.ToString();
                        if (displayVersion != null && displayVersion.ToString().Length > 0)
                            name = name + " " + displayVersion.ToString();
                    }
                }
            }
            catch { }

            return build.Length > 0 ? name + "（Build " + build + "）" : name;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && ticker != null)
            {
                ticker.Stop();
                ticker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
