using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace OutlookAlert.Setup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { MachineDeployment.RequireAdministrator(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Outlook Alert Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            try
            {
                using (var mutex = new System.Threading.Mutex(false, @"Global\FacodingOutlookAlertSetup"))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); }
                    catch (System.Threading.AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                    {
                        MessageBox.Show("Outlook Alert setup is already running on this computer.", "Outlook Alert Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 1;
                    }
                    try { return Run(args); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Setup could not start." + Environment.NewLine + ex.Message, "Outlook Alert Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 1 && args[0] == "--uninstall")
            {
                if (MessageBox.Show("Remove Outlook Alert from all Windows accounts on this computer? This removes its machine provisioning and installed user packages. Other applications and their notification settings are not changed.", "Uninstall Outlook Alert by facoding", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 0;
                using (var window = new UninstallWindow())
                {
                    Application.Run(window);
                    return window.Succeeded ? 0 : 1;
                }
            }
            if (args.Length != 0)
            {
                MessageBox.Show("Unknown setup option.", "Outlook Alert Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            Application.Run(new SetupWindow());
            return 0;
        }
    }

    internal static class Payload
    {
        internal static Stream Open(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new InvalidOperationException("The installer is missing an embedded release file. Download the complete release installer.");
            return stream;
        }

        internal static string Sha256(Stream stream)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        internal static void CheckHash(string actual, string expected, string label)
        {
            if (!Regex.IsMatch(expected ?? "", "\\A[0-9A-Fa-f]{64}\\z") ||
                !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + " does not match the installer release fingerprint. Installation stopped.");
        }

        internal static X509Certificate2 LoadCertificate()
        {
            byte[] bytes;
            using (var input = Open("OutlookAlert.Setup.Publisher.cer"))
            using (var output = new MemoryStream()) { input.CopyTo(output); bytes = output.ToArray(); }
            using (var stream = new MemoryStream(bytes)) CheckHash(Sha256(stream), PayloadInfo.CertificateSha256, "Publisher certificate");
            var certificate = new X509Certificate2(bytes);
            try
            {
                if (DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
                    throw new InvalidOperationException("The included publisher certificate is outside its validity period.");
                var basic = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                if (basic != null && basic.CertificateAuthority)
                    throw new InvalidOperationException("This installer accepts only a publisher leaf certificate, never a root CA certificate.");
                var usage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
                if (usage == null || !usage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3"))
                    throw new InvalidOperationException("The included certificate is not a code-signing publisher certificate.");
                return certificate;
            }
            catch { certificate.Dispose(); throw; }
        }

        internal static string ExtractMsix(string directory)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "OutlookAlert.msix");
            using (var input = Open("OutlookAlert.Setup.Payload.msix"))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            using (var input = File.OpenRead(path)) CheckHash(Sha256(input), PayloadInfo.MsixSha256, "Application package");
            return path;
        }
        internal static string ExtractLauncher(string directory)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "OutlookAlert-Launcher.exe");
            using (var input = Open("OutlookAlert.Setup.UserLauncher.exe"))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            using (var input = File.OpenRead(path)) CheckHash(Sha256(input), PayloadInfo.LauncherSha256, "User launcher");
            return path;
        }
    }

    internal sealed class SetupWindow : Form
    {
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly CheckBox trust;
        private readonly Button install;
        private readonly TextBox details;
        private bool busy;
        private bool installed;
        private readonly Color blue = Color.FromArgb(0, 122, 255);

        private readonly System.Collections.Generic.List<Label> wrappingLabels = new System.Collections.Generic.List<Label>();
        private bool updatingWrapWidths;

        internal SetupWindow()
        {
            SuspendLayout();
            Text = "Outlook Alert Setup 3.0.1";
            Font = new Font("Segoe UI", 10F);
            ForeColor = Color.FromArgb(28, 28, 30);
            BackColor = Color.White;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(740, 760);
            MinimumSize = new Size(420, 380);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = true;
            RightToLeft = RightToLeft.No;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location) ?? SystemIcons.Information; }
            catch { Icon = SystemIcons.Information; }

            var frame = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty,
                BackColor = Color.White
            };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(frame);

            var viewport = new Panel
            {
                Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty,
                BackColor = Color.White, Padding = Padding.Empty
            };
            var body = VerticalLayout(new Padding(28, 26, 28, 22));
            body.Dock = DockStyle.Top;
            viewport.Controls.Add(body);
            frame.Controls.Add(viewport, 0, 0);
            frame.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(229, 229, 234) }, 0, 1);

            AddRow(body, WrappedLabel("Outlook Alert", 28F, FontStyle.Bold, Color.FromArgb(28, 28, 30), new Padding(0, 0, 0, 6)));
            AddRow(body, WrappedLabel("New Outlook alerts on every display.", 13F, FontStyle.Regular, Color.FromArgb(99, 99, 102), new Padding(0, 0, 0, 18)));

            var publisher = VerticalLayout(new Padding(18));
            publisher.BackColor = Color.FromArgb(234, 243, 255);
            publisher.Margin = new Padding(0, 0, 0, 18);
            publisher.Dock = DockStyle.Fill;
            AddRow(publisher, WrappedLabel("Publisher certificate", 12F, FontStyle.Bold, ForeColor, new Padding(0, 0, 0, 8)));
            AddRow(publisher, WrappedLabel("This release uses a self-signed certificate. Setup will trust it on this computer.", 10F, FontStyle.Regular, ForeColor, new Padding(0, 0, 0, 10)));
            AddRow(publisher, WrappedLabel("Each Windows user must allow notification access once.", 10F, FontStyle.Regular, ForeColor, Padding.Empty));
            AddRow(body, publisher);

            var consent = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0, 0, 0, 18), Padding = Padding.Empty
            };
            consent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            consent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            consent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            trust = new CheckBox
            {
                AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Margin = new Padding(0, 2, 9, 0), Text = string.Empty,
                AccessibleName = "I agree to trust this publisher certificate."
            };
            var consentText = WrappedLabel("I agree to trust this publisher certificate.", 10F, FontStyle.Regular, ForeColor, Padding.Empty);
            consentText.Cursor = Cursors.Hand;
            consentText.Click += (sender, e) => { if (trust.Enabled) trust.Checked = !trust.Checked; };
            trust.CheckedChanged += (sender, e) => install.Enabled = installed || (!busy && trust.Checked);
            consent.Controls.Add(trust, 0, 0);
            consent.Controls.Add(consentText, 1, 0);
            AddRow(body, consent);

            AddRow(body, WrappedLabel("Details", 10F, FontStyle.Bold, ForeColor, new Padding(0, 0, 0, 8)));
            details = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(245, 245, 247),
                Font = new Font("Consolas", 9F), Height = 120,
                Margin = Padding.Empty
            };
            AddRow(body, details);

            // This footer stays visible while the content above scrolls at any DPI.
            var footer = VerticalLayout(new Padding(28, 14, 28, 14));
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.FromArgb(250, 250, 252);
            status = WrappedLabel("Ready to install for all users.", 9F, FontStyle.Regular, Color.FromArgb(99, 99, 102), new Padding(0, 0, 0, 9));
            AddRow(footer, status);
            progress = new ProgressBar
            {
                Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous,
                Height = 6, Margin = new Padding(0, 0, 0, 12)
            };
            AddRow(footer, progress);
            install = new Button
            {
                Text = "Install for all users", Dock = DockStyle.Fill,
                AutoSize = true, MinimumSize = new Size(0, 48),
                Padding = new Padding(16, 10, 16, 10), Margin = Padding.Empty,
                BackColor = blue, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Enabled = false, Cursor = Cursors.Hand
            };
            install.FlatAppearance.BorderSize = 0;
            install.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 102, 214);
            install.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 91, 190);
            install.Click += InstallClicked;
            AddRow(footer, install);
            var signature = new LinkLabel
            {
                Text = "facoding", AutoSize = true,
                Anchor = AnchorStyles.None, Margin = new Padding(0, 12, 0, 0),
                LinkColor = Color.FromArgb(0, 102, 204),
                ActiveLinkColor = Color.FromArgb(0, 91, 190),
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font("Segoe UI", 9F)
            };
            signature.LinkClicked += (sender, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/faCoding97") { UseShellExecute = true }); }
                catch { MessageBox.Show(this, "Open github.com/faCoding97 in your browser.", "facoding", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            AddRow(footer, signature);
            frame.Controls.Add(footer, 0, 2);

            body.SizeChanged += (sender, e) => UpdateWrapWidths();
            publisher.SizeChanged += (sender, e) => UpdateWrapWidths();
            consent.SizeChanged += (sender, e) => UpdateWrapWidths();
            footer.SizeChanged += (sender, e) => UpdateWrapWidths();
            Action updateContentWidth = () =>
            {
                // AutoSize can otherwise keep the preferred design width after the
                // vertical scrollbar appears, creating unnecessary horizontal scrolling.
                int width = Math.Max(1, viewport.ClientSize.Width);
                if (body.MaximumSize.Width != width) body.MaximumSize = new Size(width, 0);
                UpdateWrapWidths();
            };
            viewport.ClientSizeChanged += (sender, e) => updateContentWidth();
            viewport.Layout += (sender, e) => updateContentWidth();
            Shown += (sender, e) => { FitToWorkingArea(true); updateContentWidth(); };
            DpiChanged += (sender, e) =>
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => { FitToWorkingArea(false); updateContentWidth(); }));
            };
            FormClosing += (sender, e) =>
            {
                if (busy) { e.Cancel = true; status.Text = "Installation is in progress. Please wait."; }
            };
            ResumeLayout(true);
        }

        private static TableLayoutPanel VerticalLayout(Padding padding)
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 1, RowCount = 0,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Dock = DockStyle.Top, Margin = Padding.Empty, Padding = padding
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return panel;
        }

        private static void AddRow(TableLayoutPanel panel, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, row);
        }

        private Label WrappedLabel(string text, float size, FontStyle style, Color color, Padding margin)
        {
            var label = new Label
            {
                Text = text, AutoSize = true, Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", size, style), ForeColor = color,
                Margin = margin, MaximumSize = new Size(680, 0),
                UseMnemonic = false
            };
            wrappingLabels.Add(label);
            return label;
        }

        private void UpdateWrapWidths()
        {
            if (updatingWrapWidths || IsDisposed) return;
            updatingWrapWidths = true;
            try
            {
                foreach (var label in wrappingLabels)
                {
                    var parent = label.Parent as TableLayoutPanel;
                    if (parent == null || parent.ClientSize.Width <= 0) continue;
                    int column = parent.GetColumn(label);
                    int[] widths = parent.GetColumnWidths();
                    if (column < 0 || column >= widths.Length) continue;
                    int width = Math.Max(1, widths[column] - label.Margin.Horizontal);
                    if (label.MaximumSize.Width != width)
                        label.MaximumSize = new Size(width, 0);
                }
            }
            finally { updatingWrapWidths = false; }
        }

        private void FitToWorkingArea(bool center)
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int inset = Math.Max(8, DeviceDpi / 6);
            int availableWidth = Math.Max(1, area.Width - inset * 2);
            int availableHeight = Math.Max(1, area.Height - inset * 2);
            double scale = DeviceDpi / 96.0;
            MinimumSize = new Size(Math.Min((int)Math.Round(420 * scale), availableWidth),
                Math.Min((int)Math.Round(380 * scale), availableHeight));
            int width = Math.Min(Width, availableWidth);
            int height = Math.Min(Height, availableHeight);
            int x = center ? area.Left + (area.Width - width) / 2 : Math.Max(area.Left + inset, Math.Min(Left, area.Right - inset - width));
            int y = center ? area.Top + (area.Height - height) / 2 : Math.Max(area.Top + inset, Math.Min(Top, area.Bottom - inset - height));
            Bounds = new Rectangle(x, y, width, height);
        }

        private void Log(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
            details.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void Step(string message, int value)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, int>(Step), message, value); return; }
            status.Text = message; progress.Value = value; Log(message);
        }

        private async void InstallClicked(object sender, EventArgs e)
        {
            if (installed) { Close(); return; }
            if (!trust.Checked || busy) return;
            busy = true; trust.Enabled = false; install.Enabled = false;
            try
            {
                await Task.Run((Action)InstallApplication);
                installed = true;
                status.Text = "Installed for all Windows accounts. Open Outlook Alert by facoding from Start in your own account.";
                progress.Value = 100;
                install.Text = "Finish";
                install.Enabled = true;
            }
            catch (Exception ex)
            {
                Step("Installation did not complete. See details below.", 0);
                Log(ex.Message);
                MessageBox.Show(this, ex.Message, "Outlook Alert installation", MessageBoxButtons.OK, MessageBoxIcon.Error);
                trust.Enabled = true; install.Enabled = trust.Checked;
            }
            finally { busy = false; }
        }

        private void InstallApplication() { MachineDeployment.Install(Step, Log); }
    }

    internal sealed class UninstallWindow : Form
    {
        internal bool Succeeded { get; private set; }
        private bool complete;

        internal UninstallWindow()
        {
            Text = "Uninstall Outlook Alert by facoding";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 200);
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = "Removing Outlook Alert", AutoSize = true, Font = new Font("Segoe UI", 18F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) }, 0, 0);
            panel.Controls.Add(new Label { Text = "Windows is removing this app from all accounts. Please wait...", AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 18) }, 0, 1);
            panel.Controls.Add(new ProgressBar { Dock = DockStyle.Fill, Height = 6, Style = ProgressBarStyle.Marquee }, 0, 2);
            Controls.Add(panel);
            FormClosing += (sender, e) => { if (!complete) e.Cancel = true; };
            Shown += async (sender, e) =>
            {
                try
                {
                    await Task.Run((Action)MachineDeployment.Uninstall);
                    Succeeded = true;
                    MessageBox.Show(this, "Outlook Alert was removed from machine provisioning and removal was requested for its installed user packages. Other signed-in accounts may need to sign out to finish. The publisher certificate remains in Trusted People.", "Outlook Alert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    MachineDeployment.ScheduleMachineCacheRemoval();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Uninstall did not complete." + Environment.NewLine + ex.Message, "Outlook Alert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { complete = true; Close(); }
            };
        }
    }

    internal static class MachineDeployment
    {
        internal static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "facoding", "OutlookAlert");
        internal static readonly string ShortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Outlook Alert by facoding.lnk");
        internal const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\facoding.OutlookAlert";

        internal static void RequireAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Installing Outlook Alert for all Windows accounts needs administrator approval. Run this installer normally and accept the Windows prompt, or choose Run as administrator.");
        }

        internal static void Install(Action<string, int> step, Action<string> log)
        {
            RequireAdministrator();
            string temporary = Path.Combine(Path.GetTempPath(), "OutlookAlertSetup-" + Guid.NewGuid().ToString("N"));
            bool provisioned = false;
            try
            {
                step("Checking the signed application and launcher...", 8);
                SecureDirectory(temporary);
                string msix = Payload.ExtractMsix(temporary);
                string launcher = Payload.ExtractLauncher(temporary);
                // Keep read handles throughout deployment/copying. Other processes cannot
                // replace or modify a verified payload between hashing and privileged use.
                using (var packageLock = new FileStream(msix, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var launcherLock = new FileStream(launcher, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var certificate = Payload.LoadCertificate())
                {
                    Payload.CheckHash(Payload.Sha256(packageLock), PayloadInfo.MsixSha256, "Application package");
                    Payload.CheckHash(Payload.Sha256(launcherLock), PayloadInfo.LauncherSha256, "User launcher");
                    packageLock.Position = 0;
                    var package = ReadPackageIdentity(packageLock, certificate.Subject);
                    step("Approving this release's publisher for the computer...", 20);
                    using (var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine))
                    {
                        store.Open(OpenFlags.ReadWrite);
                        if (!store.Certificates.Cast<X509Certificate2>().Any(existing => existing.RawData.SequenceEqual(certificate.RawData)))
                        {
                            store.Add(certificate);
                            log("Approved the pinned publisher leaf in LocalMachine TrustedPeople.");
                        }
                        else log("The exact publisher certificate was already approved.");
                    }
                    step("Checking the previous helper for this Windows account...", 28);
                    StopOwnLegacyHelper(log);
                    SecureDirectory(InstallRoot);
                    string releaseDirectory = Path.Combine(InstallRoot, package.Version.ToString());
                    SecureDirectory(releaseDirectory);
                    string installedLauncher = Path.Combine(releaseDirectory, "OutlookAlert-Launcher.exe");
                    CopyIfDifferent(launcher, installedLauncher, PayloadInfo.LauncherSha256);
                    string cachedSetup = Path.Combine(releaseDirectory, "OutlookAlert-Setup.exe");
                    string sourceSetup = Assembly.GetExecutingAssembly().Location;
                    if (!string.Equals(Path.GetFullPath(sourceSetup), Path.GetFullPath(cachedSetup), StringComparison.OrdinalIgnoreCase))
                    {
                        using (var source = File.OpenRead(sourceSetup))
                        {
                            string sourceHash = Payload.Sha256(source);
                            CopyIfDifferent(sourceSetup, cachedSetup, sourceHash);
                        }
                    }

                    step("Preparing Outlook Alert for all Windows accounts. This may take a few minutes...", 48);
                    string name = PsLiteral(PayloadInfo.PackageName), version = PsLiteral(package.Version.ToString());
                    string command = "$ErrorActionPreference='Stop'; Add-AppxProvisionedPackage -Online -PackagePath '" + PsLiteral(msix) + "' -SkipLicense -ErrorAction Stop | Out-Null; " +
                        "$p=@(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq '" + name + "' -and [version]$_.Version -eq [version]'" + version + "' }); " +
                        "if($p.Count -ne 1){throw 'The provisioned release identity could not be resolved uniquely.'}; Write-Output ('OA_PACKAGE_FULLNAME='+$p[0].PackageName)";
                    string result = RunPowerShell(command, log, 600000);
                    provisioned = true;
                    string fullName = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => line.StartsWith("OA_PACKAGE_FULLNAME=", StringComparison.Ordinal))
                        .Select(line => line.Substring("OA_PACKAGE_FULLNAME=".Length).Trim()).SingleOrDefault();
                    var match = Regex.Match(fullName ?? "", @"\A" + Regex.Escape(PayloadInfo.PackageName) + "_" + Regex.Escape(package.Version.ToString()) + @"_x64__[a-z0-9]{13}\z", RegexOptions.IgnoreCase);
                    if (!match.Success) throw new InvalidOperationException("Windows provisioned the package, but returned an unexpected package identity. Run setup again to repair the shared launcher.");
                    string publisherId = fullName.Substring(fullName.LastIndexOf('_') + 1).ToLowerInvariant();
                    string family = PayloadInfo.PackageName + "_" + publisherId;
                    var configuration = new XDocument(new XElement("OutlookAlertDeployment",
                        new XAttribute("PackageName", PayloadInfo.PackageName), new XAttribute("FamilyName", family),
                        new XAttribute("PackageFullName", fullName), new XAttribute("ApplicationId", PayloadInfo.ApplicationId),
                        new XAttribute("MinimumVersion", package.Version.ToString())));
                    WriteConfiguration(configuration, Path.Combine(releaseDirectory, "deployment.xml"));
                    step("Creating the shared Start menu entry...", 85);
                    CreateShortcut(installedLauncher, releaseDirectory);
                    using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(UninstallKey))
                    {
                        if (key == null) throw new InvalidOperationException("Windows could not create the machine uninstall entry.");
                        key.SetValue("DisplayName", "Outlook Alert by facoding");
                        key.SetValue("DisplayVersion", package.Version.ToString());
                        key.SetValue("Publisher", "facoding");
                        key.SetValue("InstallLocation", InstallRoot);
                        key.SetValue("DisplayIcon", installedLauncher);
                        key.SetValue("UninstallString", QuoteArgument(cachedSetup) + " --uninstall");
                        key.SetValue("URLInfoAbout", "https://github.com/faCoding97");
                        key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    log("Provisioned " + fullName + " for this computer.");
                    log("Each account can open Outlook Alert by facoding from Start. Its launcher registers or updates that account when required.");
                    log("Every user must separately allow notification access and enable startup once. Provisioning never grants those permissions.");
                    step("Installed for all Windows accounts. Open Outlook Alert by facoding from Start in your own account.", 100);
                }
            }
            catch (Exception ex)
            {
                if (provisioned)
                    throw new InvalidOperationException("Windows provisioned the application, but setup could not finish its shared launcher. Run this installer again to repair the installation." + Environment.NewLine + ex.Message, ex);
                throw;
            }
            finally
            {
                try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
                catch { log("Temporary setup files could not be removed; Windows may still be using them."); }
            }
        }

        private sealed class PackageIdentity
        {
            internal Version Version;
        }

        private static PackageIdentity ReadPackageIdentity(Stream package, string certificateSubject)
        {
            using (var archive = new System.IO.Compression.ZipArchive(package, System.IO.Compression.ZipArchiveMode.Read, true))
            {
                var manifest = archive.GetEntry("AppxManifest.xml");
                if (manifest == null || manifest.Length > 1024 * 1024) throw new InvalidDataException("The application package manifest is invalid.");
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                XDocument document;
                using (var stream = manifest.Open())
                using (var reader = System.Xml.XmlReader.Create(stream, settings)) document = XDocument.Load(reader);
                XNamespace schema = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                var identity = document.Root == null ? null : document.Root.Element(schema + "Identity");
                Version version;
                if (identity == null || (string)identity.Attribute("Name") != PayloadInfo.PackageName ||
                    (string)identity.Attribute("Publisher") != certificateSubject ||
                    (string)identity.Attribute("ProcessorArchitecture") != "x64" ||
                    !Version.TryParse((string)identity.Attribute("Version"), out version))
                    throw new InvalidDataException("The pinned package does not match this installer's application identity.");
                return new PackageIdentity { Version = version };
            }
        }

        private static void SecureDirectory(string path)
        {
            string authorDirectory = Path.GetDirectoryName(InstallRoot);
            foreach (string candidate in new[] { authorDirectory, InstallRoot, path }.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Setup refuses an installation directory that is a symbolic link or junction: " + candidate);
            Directory.CreateDirectory(path);
            var security = new System.Security.AccessControl.DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var inherit = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new SecurityIdentifier(sid, null), System.Security.AccessControl.FileSystemRights.FullControl, inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), System.Security.AccessControl.FileSystemRights.ReadAndExecute, inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }

        private static void CopyIfDifferent(string source, string target, string expectedHash)
        {
            if (File.Exists(target))
            {
                using (var existing = File.OpenRead(target))
                    if (string.Equals(Payload.Sha256(existing), expectedHash, StringComparison.OrdinalIgnoreCase)) return;
            }
            File.Copy(source, target, true);
            using (var copy = File.OpenRead(target)) Payload.CheckHash(Payload.Sha256(copy), expectedHash, Path.GetFileName(target));
        }

        private static void WriteConfiguration(XDocument document, string path)
        {
            string contents = document.ToString(SaveOptions.DisableFormatting);
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == contents) return;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void CreateShortcut(string launcher, string directory)
        {
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { launcher });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { directory });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Outlook Alert by facoding. Opens for the current Windows account." });
                type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { launcher + ",0" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void StopOwnLegacyHelper(Action<string> log)
        {
            string sid;
            using (var current = WindowsIdentity.GetCurrent()) sid = current.User.Value;
            string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Faraz Outlook Alert.lnk");
            if (File.Exists(shortcut)) File.Delete(shortcut);
            int stopped = 0;
            var exactScript = new Regex(@"(?i)(?:^|[\\/\s"" ])OutlookAlert\.ps1(?:[""\s]|$)");
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId,CommandLine FROM Win32_Process WHERE Name='powershell.exe' OR Name='pwsh.exe'"))
            using (var processes = searcher.Get())
                foreach (ManagementObject process in processes)
                    using (process)
                    {
                        string command = process["CommandLine"] as string;
                        if (string.IsNullOrEmpty(command) || !exactScript.IsMatch(command)) continue;
                        try
                        {
                            using (var owner = process.InvokeMethod("GetOwnerSid", null, null))
                                if (owner == null || Convert.ToUInt32(owner["ReturnValue"]) != 0 || !string.Equals(owner["Sid"] as string, sid, StringComparison.Ordinal)) continue;
                        }
                        catch (ManagementException) { continue; }
                        catch (InvalidOperationException) { continue; }
                        try
                        {
                            using (var target = Process.GetProcessById((int)(uint)process["ProcessId"]))
                            {
                                target.Kill();
                                if (!target.WaitForExit(5000)) throw new InvalidOperationException("The previous helper for this Windows account did not stop.");
                                stopped++;
                            }
                        }
                        catch (ArgumentException) { }
                    }
            log("Legacy helper processes stopped for setup's Windows account: " + stopped + ". Other users' processes were not changed.");
        }

        internal static void Uninstall()
        {
            RequireAdministrator();
            string ownDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configurationPath = Path.Combine(ownDirectory, "deployment.xml");
            if (!File.Exists(configurationPath))
                throw new InvalidOperationException("The machine installation configuration is missing. Reinstall the current release, then uninstall it through Windows Settings.");
            XDocument document;
            var xmlSettings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = System.Xml.XmlReader.Create(configurationPath, xmlSettings)) document = XDocument.Load(reader);
            string family = document.Root == null ? null : (string)document.Root.Attribute("FamilyName");
            if (document.Root == null || document.Root.Name != "OutlookAlertDeployment" ||
                (string)document.Root.Attribute("PackageName") != PayloadInfo.PackageName ||
                !Regex.IsMatch(family ?? "", @"\AFaraz\.OutlookAlert_[a-z0-9]{13}\z"))
                throw new InvalidDataException("The machine installation identity is invalid; no package was removed.");
            string publisherId = family.Substring(family.LastIndexOf('_') + 1);
            string command = "$ErrorActionPreference='Stop'; " +
                "$provisioned=@(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq '" + PsLiteral(PayloadInfo.PackageName) + "' -and $_.PackageName.EndsWith('_" + PsLiteral(publisherId) + "',[StringComparison]::OrdinalIgnoreCase) }); " +
                "foreach($p in $provisioned){Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName -ErrorAction Stop | Out-Null}; " +
                "$registered=@(Get-AppxPackage -AllUsers -Name '" + PsLiteral(PayloadInfo.PackageName) + "' | Where-Object { $_.PackageFamilyName -eq '" + PsLiteral(family) + "' }); " +
                "foreach($p in $registered){Remove-AppxPackage -Package $p.PackageFullName -AllUsers -ErrorAction Stop}";
            RunPowerShell(command, message => { }, 600000);
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
        }

        internal static void ScheduleMachineCacheRemoval()
        {
            string root = InstallRoot;
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
            int parent = Process.GetCurrentProcess().Id;
            string command = "Wait-Process -Id " + parent + " -Timeout 60 -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '" + PsLiteral(root) + "' -Recurse -Force -ErrorAction SilentlyContinue";
            Process.Start(new ProcessStartInfo(PowerShellPath(), "-NoLogo -NoProfile -NonInteractive -Command " + QuoteArgument(command))
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
            });
        }

        private static string PowerShellPath() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"); }

        private static string RunPowerShell(string command, Action<string> log, int timeout)
        {
            var start = new ProcessStartInfo(PowerShellPath(), "-NoLogo -NoProfile -NonInteractive -Command " + QuoteArgument("[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); " + command))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Windows package deployment did not start.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Windows package deployment took too long. Check Installed apps before retrying setup.");
                }
                Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0) throw new InvalidOperationException("Windows package deployment failed:" + Environment.NewLine + stderr.Result.Trim() + Environment.NewLine + stdout.Result.Trim());
                if (!string.IsNullOrWhiteSpace(stderr.Result)) log(stderr.Result.Trim());
                return stdout.Result;
            }
        }

        private static string PsLiteral(string value) { return value.Replace("'", "''"); }
        private static string QuoteArgument(string value)
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

}
