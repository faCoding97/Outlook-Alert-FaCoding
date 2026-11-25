using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace Faraz.OutlookAlert;

internal sealed class StartupRegistration
{
    public const string TaskId = "OutlookAlertStartup";
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Invoke from the application dispatcher. Monitoring/notification consent is
    // deliberately unrelated to this OS registration operation.
    public async Task<StartupRegistrationState> SetDesiredAsync(bool desired)
    {
        await _gate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var task = await StartupTask.GetAsync(TaskId).AsTask(timeout.Token);
            var state = FromNative(task.State);
            if (StartupPolicy.ShouldRequestEnable(desired, state))
            {
                // The request can be refused; use the returned state, never the
                // preference, as evidence that startup is actually enabled.
                return FromNative(await task.RequestEnableAsync().AsTask(timeout.Token));
            }
            if (StartupPolicy.ShouldDisable(desired, state))
            {
                task.Disable();
                return FromNative(task.State);
            }
            // User and administrator choices are not bypassed with shortcuts,
            // registry Run keys, alternate task IDs, or scheduled tasks.
            return state;
        }
        finally { _gate.Release(); }
    }

    public async Task<StartupRegistrationState> GetStateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var task = await StartupTask.GetAsync(TaskId).AsTask(timeout.Token);
            return FromNative(task.State);
        }
        finally { _gate.Release(); }
    }

    private static StartupRegistrationState FromNative(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupRegistrationState.Disabled,
        StartupTaskState.DisabledByUser => StartupRegistrationState.DisabledByUser,
        StartupTaskState.Enabled => StartupRegistrationState.Enabled,
        StartupTaskState.DisabledByPolicy => StartupRegistrationState.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupRegistrationState.EnabledByPolicy,
        _ => StartupRegistrationState.Unavailable
    };
}
