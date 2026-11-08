using System;
using System.Drawing;
using System.Windows.Forms;

namespace Windows12MobileShell
{
    public class ShellForm : Form
    {
        private Label clockLabel;
        private Label dateLabel;
        private Label weatherLabel;
        private FlowLayoutPanel pinnedPanel;
        private TextBox copilotBox;

        public ShellForm()
        {
            Text = "Windows 12 Mobile";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(13, 13, 43);
            StartPosition = FormStartPosition.CenterScreen;

            var topPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(16), ColumnCount = 2 };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var clockPanel = new Panel { BackColor = Color.FromArgb(26, 42, 107), Margin = new Padding(4) };
            clockLabel = new Label { Text = "18:18", Font = new Font("Segoe UI", 36), ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            dateLabel = new Label { Text = DateTime.Now.ToString("dddd, MMMM d"), Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(160, 160, 192), Dock = DockStyle.Bottom, Height = 24 };
            clockPanel.Controls.Add(clockLabel);
            clockPanel.Controls.Add(dateLabel);
            topPanel.Controls.Add(clockPanel, 0, 0);

            var weatherPanel = new Panel { BackColor = Color.FromArgb(26, 42, 107), Margin = new Padding(4) };
            weatherLabel = new Label { Text = "64\u00b0\nClear", Font = new Font("Segoe UI", 28), ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            weatherPanel.Controls.Add(weatherLabel);
            topPanel.Controls.Add(weatherPanel, 1, 0);
            Controls.Add(topPanel);

            pinnedPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16) };
            AddPinnedApp("Edge", Color.FromArgb(0, 120, 212));
            AddPinnedApp("Files", Color.FromArgb(193, 156, 0));
            AddPinnedApp("Store", Color.FromArgb(0, 120, 212));
            AddPinnedApp("Mail", Color.FromArgb(0, 120, 212));
            AddPinnedApp("Photos", Color.FromArgb(92, 45, 145));
            AddPinnedApp("Camera", Color.FromArgb(74, 74, 90));
            AddPinnedApp("Terminal", Color.FromArgb(26, 26, 26));
            AddPinnedApp("Settings", Color.FromArgb(74, 74, 90));
            Controls.Add(pinnedPanel);

            var copilotPanel = new Panel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(16), BackColor = Color.FromArgb(42, 26, 74) };
            copilotBox = new TextBox { PlaceholderText = "Ask, search or run a command", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), BackColor = Color.FromArgb(42, 26, 74), ForeColor = Color.White, BorderStyle = BorderStyle.None };
            copilotPanel.Controls.Add(copilotBox);
            Controls.Add(copilotPanel);

            var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(26, 26, 58) };
            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill };
            foreach (var g in new[] { "Apps", "Tasks", "People", "WiFi" })
                nav.Controls.Add(new Label { Text = g, ForeColor = Color.White, Font = new Font("Segoe UI", 10), Width = 80, TextAlign = ContentAlignment.MiddleCenter });
            bottomBar.Controls.Add(nav);
            Controls.Add(bottomBar);

            var timer = new Timer { Interval = 1000 };
            timer.Tick += (s, e) => clockLabel.Text = DateTime.Now.ToString("HH:mm");
            timer.Start();
        }

        private void AddPinnedApp(string name, Color color)
        {
            var p = new Panel { Width = 80, Height = 100, Margin = new Padding(8) };
            var icon = new Panel { Width = 64, Height = 64, BackColor = color, Location = new Point(8, 0) };
            var lbl = new Label { Text = name, ForeColor = Color.White, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.TopCenter, Dock = DockStyle.Bottom, Height = 28 };
            p.Controls.Add(icon);
            p.Controls.Add(lbl);
            pinnedPanel.Controls.Add(p);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ShellForm());
        }
    }
}