using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            Console.Error.WriteLine("This layout smoke test requires Windows and .NET Framework 4.8.");
            return 2;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.Length == 0 ? Path.Combine("artifacts", "layout") : args[0]);
        Directory.CreateDirectory(output);
        var reports = new List<CaseResult>();
        var sizes = new[] { new Size(740, 710), new Size(640, 560), new Size(460, 440) };
        var scales = new[] { 1F, 1.5F, 2F };
        foreach (Size viewport in sizes)
            foreach (float scale in scales)
                reports.Add(RunCase(viewport, scale, output));
        File.WriteAllText(Path.Combine(output, "layout-results.json"), new JavaScriptSerializer().Serialize(new
        {
            description = "Owned WinForms installer layout and larger-font stress. Not a real per-monitor DPI transition or an installation test.",
            windows = Environment.OSVersion.VersionString,
            interactive = SystemInformation.UserInteractive,
            cases = reports
        }));
        int failures = reports.Count(r => r.Errors.Count != 0);
        Console.WriteLine("Layout cases: " + reports.Count + "; passed: " + (reports.Count - failures) + "; failed: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static CaseResult RunCase(Size viewport, float scale, string output)
    {
        var report = new CaseResult
        {
            RequestedWidth = viewport.Width,
            RequestedHeight = viewport.Height,
            FontScale = scale,
            Screenshot = "installer-" + viewport.Width + "x" + viewport.Height + "-font" + Math.Round(scale * 100) + ".png"
        };
        var fonts = new List<Font>();
        Form form = null;
        try
        {
            // Loading this assembly does not invoke its Program.Main, elevation manifest,
            // installation button, certificate handling, or provisioning commands.
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OutlookAlert-Setup.exe"));
            var type = assembly.GetType("OutlookAlert.Setup.SetupWindow", true);
            form = (Form)Activator.CreateInstance(type, true);
            form.ShowInTaskbar = false;
            form.Show();
            PumpLayout(form);
            report.DeviceDpi = form.DeviceDpi;
            // These cases intentionally stress text at a fixed viewport. They do not change
            // the host's DPI registry or pretend to generate a WM_DPICHANGED transition.
            form.AutoScaleMode = AutoScaleMode.None;
            var specs = Descendants(form).Prepend(form).Select(c => new FontSpec(c)).ToArray();
            foreach (var spec in specs)
            {
                var font = new Font(spec.Name, spec.Points * scale, spec.Style, GraphicsUnit.Point);
                fonts.Add(font);
                spec.Control.Font = font;
            }
            form.MinimumSize = Size.Empty;
            form.MaximumSize = Size.Empty;
            form.ClientSize = viewport;
            PumpLayout(form);
            report.ActualWidth = form.ClientSize.Width;
            report.ActualHeight = form.ClientSize.Height;

            // Verify the first consumer-visible consequence: text fits the rectangles that
            // this installer actually lays out, not just an AutoSize property or exact row.
            foreach (var label in Descendants(form).OfType<Label>().Where(c => !string.IsNullOrWhiteSpace(c.Text)))
            {
                report.TextControlsChecked++;
                Size preferred = label.GetPreferredSize(new Size(Math.Max(1, label.Width), 0));
                if (label.Height + 2 < preferred.Height)
                    report.Errors.Add("Text is vertically clipped: " + ShortText(label.Text) + "; allocated=" + label.Height + "; needed=" + preferred.Height);
                if (label.Width <= 0 || label.Height <= 0)
                    report.Errors.Add("Text has no usable layout area: " + ShortText(label.Text));
            }
            foreach (var panel in Descendants(form).OfType<ScrollableControl>().Where(c => c.AutoScroll))
                if (panel.HorizontalScroll.Visible)
                    report.Errors.Add("The responsive content requires horizontal scrolling at this viewport.");

            // Identify the primary action without invoking it or depending on its wording.
            var install = (Button)type.GetField("install", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            if (!install.Visible || install.Width <= 0 || install.Height <= 0)
                report.Errors.Add("The primary install action is not visible.");
            Rectangle full = install.RectangleToScreen(install.ClientRectangle);
            Rectangle visible = full;
            for (Control parent = install.Parent; parent != null; parent = parent.Parent)
                visible = Rectangle.Intersect(visible, parent.RectangleToScreen(parent.ClientRectangle));
            if (visible.Width + 2 < full.Width || visible.Height + 2 < full.Height)
                report.Errors.Add("The primary install action is clipped by its footer or window.");
            Size actionPreferred = install.GetPreferredSize(new Size(install.Width, 0));
            if (install.Height + 2 < actionPreferred.Height)
                report.Errors.Add("The primary install action text is clipped vertically.");

            // Draw only this form. No desktop capture, user windows, notification text,
            // account names or external apps are included in screenshots.
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(output, report.Screenshot), ImageFormat.Png);
            }
        }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            report.Errors.Add(ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (form != null) { form.Close(); form.Dispose(); }
            foreach (Font font in fonts) font.Dispose();
        }
        Console.WriteLine((report.Errors.Count == 0 ? "PASS" : "FAIL") + ": " + viewport.Width + "x" + viewport.Height + ", font " + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x; labels=" + report.TextControlsChecked);
        foreach (string error in report.Errors) Console.Error.WriteLine("  " + error);
        return report;
    }

    private static void PumpLayout(Form form)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 250)
        {
            foreach (Control control in Descendants(form).Reverse()) control.PerformLayout();
            form.PerformLayout();
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string ShortText(string value)
    {
        string text = value.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length > 90 ? text.Substring(0, 90) + "..." : text;
    }

    private sealed class FontSpec
    {
        internal readonly Control Control;
        internal readonly string Name;
        internal readonly float Points;
        internal readonly FontStyle Style;
        internal FontSpec(Control control) { Control = control; Name = control.Font.Name; Points = control.Font.SizeInPoints; Style = control.Font.Style; }
    }

    public sealed class CaseResult
    {
        public int RequestedWidth { get; set; }
        public int RequestedHeight { get; set; }
        public int ActualWidth { get; set; }
        public int ActualHeight { get; set; }
        public float FontScale { get; set; }
        public int DeviceDpi { get; set; }
        public int TextControlsChecked { get; set; }
        public string Screenshot { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
