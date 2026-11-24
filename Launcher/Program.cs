using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace Facoding.OutlookAlert.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length != 0)
            {
                MessageBox.Show("Open Outlook Alert from the Start menu.", "Outlook Alert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            using (var identity = WindowsIdentity.GetCurrent())
                if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("Open Outlook Alert normally from the Start menu, without Run as administrator. It must use your own Windows notification permissions.", "Open Outlook Alert normally", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
            using (var mutex = new Mutex(true, @"Local\FacodingOutlookAlertLauncher", out bool first))
            {
                if (!first) return 0;
                Application.Run(new LaunchWindow());
            }
            return 0;
        }
    }

    internal sealed class LaunchWindow : Form
    {
        private readonly Label status;
        private bool complete;

        internal LaunchWindow()
        {
            Text = "Outlook Alert";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 190);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.White;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = new Label { Text = "Opening Outlook Alert", AutoSize = true, Font = new Font("Segoe UI", 18F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) };
            status = new Label { Text = "Preparing your Windows account...", AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(460, 0), Margin = new Padding(0, 0, 0, 18) };
            var progress = new ProgressBar { Dock = DockStyle.Fill, Height = 6, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            panel.Controls.Add(heading, 0, 0); panel.Controls.Add(status, 0, 1); panel.Controls.Add(progress, 0, 2);
            Controls.Add(panel);
            Shown += async (sender, e) => await LaunchAsync();
            FormClosing += (sender, e) => { if (!complete) { e.Cancel = true; status.Text = "Please wait while Windows finishes preparing the application."; } };
        }

        private async Task LaunchAsync()
        {
            try
            {
                var configuration = DeploymentConfiguration.Load();
                await Task.Run(() => EnsureUserRegistration(configuration));
                Process.Start(new ProcessStartInfo("explorer.exe", Quote("shell:AppsFolder\\" + configuration.FamilyName + "!" + configuration.ApplicationId)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Outlook Alert could not be opened for this Windows account." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine + "Try signing out and signing in again. If it still fails, ask an administrator to reinstall Outlook Alert for all users.", "Outlook Alert", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { complete = true; Close(); }
        }

        private static void EnsureUserRegistration(DeploymentConfiguration configuration)
        {
            string name = PsLiteral(configuration.PackageName), family = PsLiteral(configuration.FamilyName);
            string required = PsLiteral(configuration.MinimumVersion.ToString());
            string command = "$ErrorActionPreference='Stop'; $p=@(Get-AppxPackage -Name '" + name + "' | Where-Object { $_.PackageFamilyName -eq '" + family + "' } | Sort-Object {[version]$_.Version} -Descending); " +
                "if($p.Count -eq 0 -or [version]$p[0].Version -lt [version]'" + required + "'){ Add-AppxPackage -MainPackage '" + family + "' -RegisterByFamilyName -ForceTargetApplicationShutdown -ErrorAction Stop }; " +
                "$p=@(Get-AppxPackage -Name '" + name + "' | Where-Object { $_.PackageFamilyName -eq '" + family + "' } | Sort-Object {[version]$_.Version} -Descending); " +
                "if($p.Count -eq 0 -or [version]$p[0].Version -lt [version]'" + required + "'){throw 'The provisioned release is not registered for this account yet.'}";
            RunPowerShell(command);
        }

        private static void RunPowerShell(string command)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var start = new ProcessStartInfo(powershell, "-NoLogo -NoProfile -NonInteractive -Command " + Quote("[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); " + command))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Windows did not start application registration.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(180000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Windows is taking too long to register the application. Sign out and sign in again before retrying.");
                }
                Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Result.Trim() + Environment.NewLine + stdout.Result.Trim());
            }
        }

        private static string PsLiteral(string value) { return value.Replace("'", "''"); }
        private static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                result.Append('\\', slashes).Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }

    internal sealed class DeploymentConfiguration
    {
        internal string PackageName, FamilyName, ApplicationId;
        internal Version MinimumVersion;

        internal static DeploymentConfiguration Load()
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "deployment.xml");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            XDocument document;
            using (var reader = XmlReader.Create(path, settings)) document = XDocument.Load(reader);
            var root = document.Root;
            if (root == null || root.Name != "OutlookAlertDeployment") throw new InvalidDataException("The machine installation configuration is invalid.");
            var configuration = new DeploymentConfiguration
            {
                PackageName = (string)root.Attribute("PackageName"),
                FamilyName = (string)root.Attribute("FamilyName"),
                ApplicationId = (string)root.Attribute("ApplicationId")
            };
            if (configuration.PackageName != "Faraz.OutlookAlert" || configuration.ApplicationId != "App" ||
                !Regex.IsMatch(configuration.FamilyName ?? "", @"\AFaraz\.OutlookAlert_[a-z0-9]{13}\z"))
                throw new InvalidDataException("The machine installation identity is invalid.");
            if (!Version.TryParse((string)root.Attribute("MinimumVersion"), out configuration.MinimumVersion))
                throw new InvalidDataException("The machine installation version is invalid.");
            return configuration;
        }
    }
}
