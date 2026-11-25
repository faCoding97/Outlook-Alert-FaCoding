namespace Faraz.OutlookAlert;

// Kept independent of WinRT so sign-in decisions can be verified on any host.
internal enum StartupRegistrationState
{
    Disabled,
    DisabledByUser,
    Enabled,
    DisabledByPolicy,
    EnabledByPolicy,
    Unavailable
}

internal static class StartupPolicy
{
    public static bool ShouldShowSettings(bool backgroundStartup, bool forceSetup) =>
        forceSetup || !backgroundStartup;

    public static bool ShouldSignalExistingInstance(bool backgroundStartup) => !backgroundStartup;

    public static bool ShouldRequestEnable(bool desired, StartupRegistrationState state) =>
        desired && state == StartupRegistrationState.Disabled;

    public static bool ShouldDisable(bool desired, StartupRegistrationState state) =>
        !desired && state == StartupRegistrationState.Enabled;

    public static bool IsEnabled(StartupRegistrationState state) =>
        state is StartupRegistrationState.Enabled or StartupRegistrationState.EnabledByPolicy;

    public static string Describe(StartupRegistrationState state) => state switch
    {
        StartupRegistrationState.Enabled => "Startup: enabled. Outlook Alert will run when you sign in to Windows.",
        StartupRegistrationState.EnabledByPolicy => "Startup: enabled by your organization.",
        StartupRegistrationState.DisabledByUser => "Startup: disabled in Windows. Open Windows startup settings and switch Outlook Alert on.",
        StartupRegistrationState.DisabledByPolicy => "Startup: blocked by your organization or unsupported on this device. Contact your administrator.",
        StartupRegistrationState.Disabled => "Startup: off. Choose Enable automatic startup to run Outlook Alert when you sign in.",
        _ => "Startup: could not be checked. Open the installed Outlook Alert app from Start and try again."
    };
}
