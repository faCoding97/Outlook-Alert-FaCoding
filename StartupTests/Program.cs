using Faraz.OutlookAlert;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string scenario)
{
    checks++;
    if (!condition) throw new Exception(scenario);
}

Check(!StartupPolicy.ShouldShowSettings(true, false), "Sign-in startup stays in the tray.");
Check(StartupPolicy.ShouldShowSettings(false, false), "Opening the app from Start shows settings.");
Check(StartupPolicy.ShouldShowSettings(true, true), "Explicit setup remains visible even with startup activation.");
Check(!StartupPolicy.ShouldSignalExistingInstance(true), "Duplicate sign-in activation does not raise settings in a running instance.");
Check(StartupPolicy.ShouldSignalExistingInstance(false), "Opening an already-running app raises its settings.");
Check(StartupPolicy.ShouldRequestEnable(true, StartupRegistrationState.Disabled), "A new install can register sign-in startup before monitoring is enabled.");
Check(!StartupPolicy.ShouldRequestEnable(false, StartupRegistrationState.Disabled), "An opt-out stays disabled on later launches.");
Check(StartupPolicy.ShouldDisable(false, StartupRegistrationState.Enabled), "Turning startup off disables an ordinary enabled task.");
Check(!StartupPolicy.ShouldDisable(true, StartupRegistrationState.Enabled), "A wanted, enabled task needs no disable operation.");
foreach (var state in new[] { StartupRegistrationState.DisabledByUser, StartupRegistrationState.DisabledByPolicy, StartupRegistrationState.EnabledByPolicy })
{
    Check(!StartupPolicy.ShouldRequestEnable(true, state), "Explicit Windows or organization choice is never overridden: " + state);
    Check(!StartupPolicy.ShouldDisable(false, state), "Organization and Windows-owned state is not mutated: " + state);
}
Check(StartupPolicy.IsEnabled(StartupRegistrationState.EnabledByPolicy), "An organization-enabled task is reported as enabled.");
Check(!StartupPolicy.IsEnabled(StartupRegistrationState.DisabledByUser), "A refused startup request must not be reported as enabled.");
Check(!StartupPolicy.IsEnabled(StartupRegistrationState.Unavailable), "A status read failure must not imply startup is enabled.");
Check(StartupPolicy.Describe(StartupRegistrationState.DisabledByUser).Contains("Windows startup settings"), "User-disabled startup has an actionable next step.");
Check(StartupPolicy.Describe(StartupRegistrationState.DisabledByPolicy).Contains("administrator"), "A policy block identifies who can resolve it.");
var fresh = JsonSerializer.Deserialize<AlertSettings>("{}")!;
Check(fresh.StartAtSignIn && !fresh.MonitoringEnabled, "First launch enables startup without granting monitoring permission.");
var upgraded = JsonSerializer.Deserialize<AlertSettings>("{\"MonitoringEnabled\":true}")!;
Check(upgraded.MonitoringEnabled && upgraded.StartAtSignIn && upgraded.SoundId == "chime", "A 3.1 settings file preserves monitoring and gains startup/sound defaults.");
var preferences = new AlertSettings { MonitoringEnabled = true, StartAtSignIn = false, SoundId = "silent" };
var restored = JsonSerializer.Deserialize<AlertSettings>(JsonSerializer.Serialize(preferences))!;
Check(restored.MonitoringEnabled && !restored.StartAtSignIn && restored.SoundId == "silent", "Sound and startup opt-outs survive saving and restart.");
Check(AlertSoundCatalog.Normalize(JsonSerializer.Deserialize<AlertSettings>("{\"SoundId\":null}")!.SoundId) == "chime", "A damaged null sound selection recovers to the default.");
Console.WriteLine($"{checks} startup and settings checks passed.");
