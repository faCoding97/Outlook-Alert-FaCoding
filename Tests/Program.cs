using Faraz.OutlookAlert;
using System.Reflection;
using System.Security.Cryptography;

if (args.Length == 5 && args[0] == "--validate-installer")
{
    // Load metadata/resources only: never invoke the Windows installer entrypoint.
    var release = Assembly.LoadFile(Path.GetFullPath(args[1]));
    var pins = release.GetType("OutlookAlert.Setup.PayloadInfo", throwOnError: true)!;
    foreach (var item in new[] {
        (Resource: "OutlookAlert.Setup.Payload.msix", Pin: "MsixSha256", Path: args[2]),
        (Resource: "OutlookAlert.Setup.Publisher.cer", Pin: "CertificateSha256", Path: args[3]),
        (Resource: "OutlookAlert.Setup.UserLauncher.exe", Pin: "LauncherSha256", Path: args[4]) })
    {
        using var stream = release.GetManifestResourceStream(item.Resource)
            ?? throw new Exception("Missing release resource: " + item.Resource);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        var expected = (string)pins.GetField(item.Pin, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        using var releaseFile = File.OpenRead(item.Path);
        var expectedRelease = Convert.ToHexString(SHA256.HashData(releaseFile));
        if (actual != expected || expected != expectedRelease)
            throw new Exception("Embedded payload is not the intended current release: " + item.Resource);
        Console.WriteLine("PASS: embedded " + item.Resource + " matches its pin and the intended current release file");
    }
    return;
}


if (args.Length == 2 && args[0] == "--validate-launcher-config")
{
    var binary = Path.GetFullPath(args[1]);
    var configPath = Path.Combine(Path.GetDirectoryName(binary)!, "deployment.xml");
    if (File.Exists(configPath)) throw new Exception("Refusing to overwrite an existing deployment configuration");
    var release = Assembly.LoadFile(binary);
    var configType = release.GetType("Facoding.OutlookAlert.Launcher.DeploymentConfiguration", true)!;
    var load = configType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
    const string good = "<OutlookAlertDeployment PackageName=\"Faraz.OutlookAlert\" FamilyName=\"Faraz.OutlookAlert_abcde12345678\" ApplicationId=\"App\" MinimumVersion=\"3.0.0.0\" />";
    var cases = new[] {
        (Name: "Valid intended app", Xml: good, Accept: true),
        (Name: "Wrong target app", Xml: good.Replace("PackageName=\"Faraz.OutlookAlert\"", "PackageName=\"Other.App\""), Accept: false),
        (Name: "Unsafe family", Xml: good.Replace("Faraz.OutlookAlert_abcde12345678", "Faraz.OutlookAlert_abcde12345678'; bad"), Accept: false),
        (Name: "Wrong activation ID", Xml: good.Replace("ApplicationId=\"App\"", "ApplicationId=\"Other\""), Accept: false),
        (Name: "Invalid minimum version", Xml: good.Replace("3.0.0.0", "not-a-version"), Accept: false),
        (Name: "DTD is prohibited", Xml: "<!DOCTYPE OutlookAlertDeployment [<!ENTITY app 'Faraz.OutlookAlert'>]>" + good.Replace("PackageName=\"Faraz.OutlookAlert\"", "PackageName=\"&app;\""), Accept: false)
    };
    try
    {
        foreach (var test in cases)
        {
            File.WriteAllText(configPath, test.Xml);
            bool accepted;
            try { _ = load.Invoke(null, null); accepted = true; }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException || ex.InnerException is System.Xml.XmlException) { accepted = false; }
            if (accepted != test.Accept) throw new Exception("FAIL: " + test.Name);
            Console.WriteLine("PASS: launcher configuration / " + test.Name);
        }
    }
    finally { File.Delete(configPath); }
    return;
}


const string Outlook = "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows";
var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
NotificationStamp Mail(uint id, long created = 100) => new(Outlook, id, created);
var gate = new NotificationGate(new[] { Outlook });
Check(!gate.AcceptsApplication("TelegramDesktop"), "Telegram is rejected");
Check(!gate.AcceptsApplication("WhatsApp"), "WhatsApp is rejected");
Check(!gate.AcceptsApplication("Telegram: a message mentions Outlook"), "Text containing Outlook cannot substitute for application identity");
Check(!gate.AcceptsApplication(Outlook + ".fake"), "Near-matching application IDs are rejected");
Check(!gate.AcceptsApplication(null), "Missing identity is rejected");
Check(gate.AcceptsApplication(Outlook), "The installed Outlook identity is accepted");
Check(gate.ApplySnapshot(new[] { Mail(1), new NotificationStamp("WhatsApp", 2, 100) }, true) == 0, "Existing notifications are baselined without alerts");
Check(gate.KnownCount == 1, "Other-app notifications do not enter Outlook state");
Check(gate.ApplySnapshot(new[] { Mail(1), new NotificationStamp("TelegramDesktop", 3, 101) }) == 0, "An unrelated notification event cannot create a popup");
Check(gate.ApplySnapshot(new[] { Mail(1), Mail(2, 102) }) == 1, "A fresh Outlook notification produces one alert");
Check(gate.ApplySnapshot(new[] { Mail(2, 102), Mail(1), Mail(2, 102) }) == 0, "Reordered snapshots and duplicates do not re-alert");
Check(gate.ApplySnapshot(new[] { Mail(2, 103) }) == 1, "A reused notification ID with a new creation timestamp is new");
Check(gate.KnownCount == 1, "Dismissed IDs are released from memory");
Check(gate.ApplySnapshot(Array.Empty<NotificationStamp>()) == 0 && gate.KnownCount == 0, "Dismissing notifications does not trigger an alert");
var noOutlook = new NotificationGate(Array.Empty<string>());
Check(noOutlook.ApplySnapshot(new[] { Mail(1) }) == 0, "No discovered Outlook identity fails closed");
// Incremental arrivals must retain earlier IDs instead of treating each event as a full snapshot.
bool Added(NotificationGate target, NotificationStamp stamp) => target.TryAcceptAdded(stamp);
var arrivals = new NotificationGate(new[] { Outlook });
arrivals.ApplySnapshot(new[] { Mail(10, 10) }, baseline: true);
Check(!Added(arrivals, Mail(10, 10)), "Baseline IDs remain suppressed in the direct event path");
Check(Added(arrivals, Mail(11, 11)), "A direct new Outlook arrival is accepted");
Check(Added(arrivals, Mail(12, 12)), "A second direct arrival is accepted");
Check(!Added(arrivals, Mail(11, 11)), "Alternating repeated IDs do not re-alert");
Check(!Added(arrivals, new NotificationStamp("WhatsApp", 13, 13)), "Direct WhatsApp arrivals are rejected");
Check(!Added(arrivals, new NotificationStamp("TelegramDesktop", 14, 14)), "Direct Telegram arrivals are rejected");
Check(Added(arrivals, Mail(11, 15)), "An ID reused with a fresh creation time is accepted");
var burst = new NotificationGate(new[] { Outlook });
for (uint id = 1; id <= 5_000; id++)
    if (!Added(burst, Mail(id, id))) throw new Exception("A new arrival was incorrectly rejected");
Check(burst.KnownCount <= 4_096, "Direct arrival history stays bounded during a long notification burst");
Check(!Added(burst, Mail(5_000, 5_000)), "The newest arrival remains protected from duplicates after trimming");
Console.WriteLine($"{passed} behavior checks passed.");
