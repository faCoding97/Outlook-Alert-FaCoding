using System;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace Faraz.OutlookAlert;

internal static class StartupActivation
{
    // AppInstance returns activation data only once. Call at the start of Main,
    // before singleton redirection or creating the WPF Application.
    public static bool IsBackgroundStartup(string[] arguments)
    {
        if (arguments.Any(x => x.Equals("--startup", StringComparison.OrdinalIgnoreCase))) return true;
        try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
        catch { return false; } // Direct/unpackaged launch has no activation payload.
    }
}
