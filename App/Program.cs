using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Forms = System.Windows.Forms;

namespace Faraz.OutlookAlert;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool backgroundStartup = StartupActivation.IsBackgroundStartup(args);
        using var singleton = new Mutex(true, @"Local\FarazOutlookAlertNativeV2", out bool first);
        if (!first)
        {
            if (StartupPolicy.ShouldSignalExistingInstance(backgroundStartup))
            {
                try { using var signal = EventWaitHandle.OpenExisting(@"Local\FarazOutlookAlertNativeShow"); signal.Set(); }
                catch { }
            }
            return;
        }
        var application = new AlertApplication(args, backgroundStartup);
        application.Run();
    }
}

internal sealed class AlertApplication : System.Windows.Application
{
    private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FarazOutlookAlertNative");
    private static readonly string StatePath = Path.Combine(StateDirectory, "settings.json");
    private readonly string[] _args;
    private readonly bool _backgroundStartup;
    private readonly StartupRegistration _startupRegistration = new();
    private Button? _startupEnableButton, _startupDisableButton;
    private const int MaximumQueuedNotifications = 1024;
    private readonly object _callbackIngressLock = new();
    private readonly Queue<PendingNotification> _callbackIngress = new();
    private readonly HashSet<uint> _callbackIngressIds = new();
    private bool _callbackPumpScheduled;
    private int _callbackOverflowEpoch;
    private Exception? _callbackFailure;
    private int _callbackFailureEpoch;
    private readonly Queue<PendingNotification> _notificationQueue = new();
    private readonly HashSet<uint> _pendingNotificationIds = new();
    private bool _priming, _processingNotifications, _queueRecoveryRequested;
    private int _sessionEpoch;
    private TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs>? _notificationHandler;
    private readonly HashSet<string> _outlookIds = new(StringComparer.OrdinalIgnoreCase);
    private NotificationGate? _gate;
    private UserNotificationListener? _listener;
    private bool _subscribed, _running, _exiting, _enabling;
    private Forms.NotifyIcon? _tray;
    private Window? _settingsWindow;
    private TextBlock? _statusLabel, _detailLabel, _startupLabel;
    private Button? _enableButton;
    private readonly PopupPresenter _popups = new();
    private readonly AlertSoundPlayer _sounds;
    private TextBlock? _soundLabel;
    private string _soundStatus = "Plays once for new email. Uses your Windows volume and output device.";
    private readonly AlertSettings _settings;
    private string _status = "Alerts are off";
    private string _detail = "Enable monitoring to start.";
    private readonly EventWaitHandle _showSignal = new(false, EventResetMode.AutoReset, @"Local\FarazOutlookAlertNativeShow");
    private RegisteredWaitHandle? _showRegistration;

    public AlertApplication(string[] args, bool backgroundStartup = false)
    {
        _args = args;
        _backgroundStartup = backgroundStartup;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SessionEnding += (_, _) => ExitApplication();
        _settings = ReadSettings();
        _settings.SoundId = AlertSoundCatalog.Normalize(_settings.SoundId);
        _sounds = new AlertSoundPlayer(ex =>
        {
            LogError("sound_playback", ex);
            _soundStatus = "Sound could not play. Check your Windows volume and output device, then try Test sound.";
            if (_soundLabel != null) _soundLabel.Text = _soundStatus;
        });
        _popups.Closed += () => _sounds.Stop();
        DispatcherUnhandledException += (_, e) =>
        {
            LogError("unhandled_ui", e.Exception);
            StopMonitoring("Monitoring stopped", "An unexpected error occurred. Open settings and enable monitoring again.");
            ShowSettings();
            e.Handled = true;
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CreateTray();
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, Timeout.Infinite, false);
        Log("app_started;activation=" + (_backgroundStartup ? "sign_in" : "manual"));
        bool forceSetup = _args.Any(x => x.Equals("--setup", StringComparison.OrdinalIgnoreCase));
        bool showSettings = StartupPolicy.ShouldShowSettings(_backgroundStartup, forceSetup);
        if (showSettings) ShowSettings();
        // Register independently of Outlook discovery or notification permission.
        // A saved opt-out is not reapplied, so a later Windows setting is respected.
        if (_settings.StartAtSignIn) await EnableStartupAsync();
        else await RefreshStartupStatusAsync();
        try
        {
            if (_settings.MonitoringEnabled)
            {
                bool enabled = await EnableMonitoringAsync(false);
                if (!enabled && showSettings) ShowSettings();
                else if (!enabled) ShowBackgroundSetupNotice();
            }
            else if (!showSettings) ShowBackgroundSetupNotice();
            await RefreshStartupStatusAsync();
            if (_args.Any(x => x.Equals("--test", StringComparison.OrdinalIgnoreCase))) ShowAlert(1, true);
        }
        catch (Exception ex)
        {
            LogError("startup", ex);
            StopMonitoring("Setup needs attention", "The listener could not start. Open settings to retry.");
            if (showSettings) ShowSettings();
            else ShowBackgroundSetupNotice();
        }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings and status", null, (_, _) => Dispatcher.BeginInvoke(ShowSettings));
        menu.Items.Add("Test alert on all screens", null, (_, _) => Dispatcher.BeginInvoke(() => ShowAlert(1, true)));
        menu.Items.Add("Open Outlook", null, (_, _) => OpenOutlook());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Outlook Alert", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication));
        _tray = new Forms.NotifyIcon
        {
            Text = "Outlook Alert: monitoring off",
            Icon = TrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowSettings);
    }

    private static System.Drawing.Icon TrayIcon()
    {
        try
        {
            if (Environment.ProcessPath is string path)
                return System.Drawing.Icon.ExtractAssociatedIcon(path) ?? System.Drawing.SystemIcons.Information;
        }
        catch { }
        return System.Drawing.SystemIcons.Information;
    }

    private async Task<bool> DiscoverOutlookAsync()
    {
        _outlookIds.Clear();
        var manager = new PackageManager();
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            if (!string.Equals(package.Id.FamilyName, "Microsoft.OutlookForWindows_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase)) continue;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var apps = await package.GetAppListEntriesAsync().AsTask(timeout.Token);
            foreach (var entry in apps)
            {
                string? id = entry.AppUserModelId;
                if (!string.IsNullOrWhiteSpace(id)) _outlookIds.Add(id);
            }
        }
        _gate = new NotificationGate(_outlookIds);
        Log("outlook_identity_count=" + _outlookIds.Count);
        return _outlookIds.Count != 0;
    }

    private async Task<bool> EnableMonitoringAsync(bool requestAccess)
    {
        if (_enabling || _running) return _running;
        _enabling = true;
        int epoch = Interlocked.Increment(ref _sessionEpoch);
        ClearCallbackIngress();
        _notificationQueue.Clear();
        _pendingNotificationIds.Clear();
        _queueRecoveryRequested = false;
        _priming = false;
        if (_enableButton != null) _enableButton.IsEnabled = false;
        try
        {
            SetStatus("Checking setup", "Finding the installed New Outlook application...");
            try { _ = Package.Current.Id.Name; }
            catch
            {
                StopMonitoring("Installation required", "Run the installer, then open Outlook Alert from Start.");
                return false;
            }
            if (!await DiscoverOutlookAsync())
            {
                StopMonitoring("New Outlook was not found", "Install New Outlook for Windows for this Windows account, then try again.");
                return false;
            }
            if (!IsCurrentSession(epoch)) return false;
            _listener = UserNotificationListener.Current;
            var access = _listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed && requestAccess)
            {
                SetStatus("Waiting for permission", "Choose Allow in the Windows notification-access prompt.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                // This method runs on the WPF dispatcher; never block it with Wait/Result.
                access = await _listener.RequestAccessAsync().AsTask(timeout.Token);
            }
            if (!IsCurrentSession(epoch)) return false;
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                StopMonitoring("Notification access is not enabled", "Allow Outlook Alert under Windows Settings > Privacy & security > Notifications, then enable monitoring here.");
                return false;
            }
            // Startup reads a complete snapshot. Queue event IDs while the one-time
            // baseline/subscription gap is reconciled, so incremental updates cannot race it.
            var baseline = await ReadOutlookStampsAsync();
            if (!IsCurrentSession(epoch)) return false;
            _gate!.ApplySnapshot(baseline, baseline: true);
            _priming = true;
            try
            {
                _notificationHandler = (sender, args) => NotificationChanged(sender, args, epoch);
                _listener.NotificationChanged += _notificationHandler;
                _subscribed = true;
            }
            catch (Exception ex)
            {
                LogError("subscribe", ex);
                StopMonitoring("Alerts could not start", "Enable monitoring again. If the problem continues, open the log.");
                return false;
            }
            var gapNotifications = await ReadOutlookStampsAsync();
            if (!IsCurrentSession(epoch)) return false;
            int gapAdded = 0;
            foreach (var stamp in gapNotifications)
                if (_gate.TryAcceptAdded(stamp)) gapAdded++;
            _running = true;
            _priming = false;
            _settings.MonitoringEnabled = true;
            SaveSettings();
            SetStatus("Monitoring Outlook", "New Outlook notifications will appear on all screens.");
            Log("monitoring_ready;baseline_count=" + _gate.KnownCount);
            if (gapAdded > 0)
            {
                ShowAlert(gapAdded, false);
                Log("startup_gap_outlook_notifications_added=" + gapAdded);
            }
            _ = DrainNotificationQueueAsync();
            return _running;
        }
        catch (Exception ex)
        {
            if (!IsCurrentSession(epoch)) return false;
            LogError("enable", ex);
            StopMonitoring("Monitoring could not start", "Windows denied access or a notification operation failed. Error code: " + Code(ex) + ". Open the diagnostic log for details.");
            return false;
        }
        finally
        {
            _enabling = false;
            if (_enableButton != null) _enableButton.IsEnabled = true;
        }
    }

    private async Task<List<NotificationStamp>> ReadOutlookStampsAsync()
    {
        if (_listener == null || _listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            throw new UnauthorizedAccessException("Notification permission is unavailable.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(timeout.Token);
        var result = new List<NotificationStamp>();
        foreach (var notification in notifications)
        {
            try
            {
                // Check trusted application identity before inspecting anything else.
                string appId = notification.AppInfo.AppUserModelId;
                if (_gate!.AcceptsApplication(appId))
                    result.Add(new NotificationStamp(appId, notification.Id, notification.CreationTime.UtcDateTime.Ticks));
            }
            catch (Exception ex) { LogError("notification_identity", ex); }
        }
        return result;
    }

    private readonly record struct PendingNotification(
        UserNotificationListener Listener, uint Id, int Epoch,
        long CallbackTimestamp, DateTimeOffset CallbackUtc);

    private readonly record struct NotificationLookup(
        NotificationStamp? Stamp, long StartedTimestamp, long CompletedTimestamp);

    private bool IsCurrentSession(int epoch) => !_exiting && epoch == Volatile.Read(ref _sessionEpoch);

    private void NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args, int epoch)
    {
        // Bound and coalesce BEFORE posting to WPF. A busy dispatcher must not accumulate
        // one operation per system notification, even when none belong to Outlook.
        long received = Stopwatch.GetTimestamp();
        try
        {
            if (!IsCurrentSession(epoch) || args.ChangeKind != UserNotificationChangedKind.Added) return;
            var pending = new PendingNotification(sender, args.UserNotificationId, epoch, received, DateTimeOffset.UtcNow);
            bool schedulePump;
            lock (_callbackIngressLock)
            {
                if (!IsCurrentSession(epoch)) return;
                if (_callbackIngressIds.Contains(pending.Id)) return;
                if (_callbackIngress.Count >= MaximumQueuedNotifications)
                {
                    _callbackOverflowEpoch = epoch;
                }
                else
                {
                    _callbackIngress.Enqueue(pending);
                    _callbackIngressIds.Add(pending.Id);
                }
                schedulePump = !_callbackPumpScheduled;
                if (schedulePump) _callbackPumpScheduled = true;
            }
            if (schedulePump) ScheduleCallbackPump();
        }
        catch (Exception ex)
        {
            bool schedulePump;
            lock (_callbackIngressLock)
            {
                if (!IsCurrentSession(epoch)) return;
                // Retain one failure instead of posting an unbounded error-operation stream.
                _callbackFailure ??= ex;
                _callbackFailureEpoch = epoch;
                schedulePump = !_callbackPumpScheduled;
                if (schedulePump) _callbackPumpScheduled = true;
            }
            if (schedulePump) ScheduleCallbackPump();
        }
    }

    private void ScheduleCallbackPump()
    {
        try
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                lock (_callbackIngressLock)
                {
                    _callbackIngress.Clear();
                    _callbackIngressIds.Clear();
                    _callbackOverflowEpoch = 0;
                    _callbackFailure = null;
                    _callbackPumpScheduled = false;
                }
                return;
            }
            _ = Dispatcher.InvokeAsync(PumpCallbackIngress, DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            lock (_callbackIngressLock) _callbackPumpScheduled = false;
            if (!_exiting) LogError("callback_handoff", ex);
        }
    }

    private void PumpCallbackIngress()
    {
        PendingNotification[] batch;
        int overflowEpoch, failureEpoch;
        Exception? failure;
        lock (_callbackIngressLock)
        {
            batch = _callbackIngress.ToArray();
            _callbackIngress.Clear();
            _callbackIngressIds.Clear();
            overflowEpoch = _callbackOverflowEpoch;
            _callbackOverflowEpoch = 0;
            failure = _callbackFailure;
            failureEpoch = _callbackFailureEpoch;
            _callbackFailure = null;
            // Keep the token until this pump finishes; callbacks can fill the bounded
            // next batch without posting more operations while this one is running.
        }
        try
        {
            if (failure != null && IsCurrentSession(failureEpoch))
                HandleNotificationFailure("notification_event", failure);
            foreach (var pending in batch) EnqueueNotification(pending);
            if (overflowEpoch != 0 && IsCurrentSession(overflowEpoch) && (_running || _priming))
                _queueRecoveryRequested = true;
            if (_running && !_priming) _ = DrainNotificationQueueAsync();
        }
        finally
        {
            bool scheduleAgain;
            lock (_callbackIngressLock)
            {
                scheduleAgain = _callbackIngress.Count != 0 || _callbackOverflowEpoch != 0 || _callbackFailure != null;
                if (!scheduleAgain) _callbackPumpScheduled = false;
            }
            if (scheduleAgain) ScheduleCallbackPump();
        }
    }

    private void ClearCallbackIngress()
    {
        lock (_callbackIngressLock)
        {
            _callbackIngress.Clear();
            _callbackIngressIds.Clear();
            _callbackOverflowEpoch = 0;
            _callbackFailure = null;
            _callbackFailureEpoch = 0;
            // A previously posted pump remains responsible for its token. Reusing it
            // after a session reset avoids accumulating stale dispatcher operations.
        }
    }

    private void EnqueueNotification(PendingNotification pending)
    {
        if (!IsCurrentSession(pending.Epoch) || (!_running && !_priming)) return;
        if (_pendingNotificationIds.Contains(pending.Id)) return;
        if (_notificationQueue.Count >= MaximumQueuedNotifications)
        {
            // A notification storm gets one bounded, event-driven recovery snapshot after
            // the queue drains. Normal mail never enumerates the whole notification center.
            _queueRecoveryRequested = true;
            return;
        }
        _pendingNotificationIds.Add(pending.Id);
        _notificationQueue.Enqueue(pending);
        if (!_priming) _ = DrainNotificationQueueAsync();
    }

    private static NotificationLookup LookupNotification(PendingNotification pending, NotificationGate gate)
    {
        long started = Stopwatch.GetTimestamp();
        if (pending.Listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            throw new UnauthorizedAccessException("Notification permission is unavailable.");
        // This synchronous WinRT call runs on a thread-pool MTA thread, never the UI or
        // Windows event callback. It reads one ID, not the notification-center backlog.
        var notification = pending.Listener.GetNotification(pending.Id);
        NotificationStamp? stamp = null;
        if (notification != null)
        {
            string appId = notification.AppInfo.AppUserModelId;
            if (gate.AcceptsApplication(appId))
                stamp = new NotificationStamp(appId, notification.Id, notification.CreationTime.UtcDateTime.Ticks);
        }
        else if (pending.Listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
        {
            throw new UnauthorizedAccessException("Notification permission is unavailable.");
        }
        // A notification removed before lookup is documented to return null.
        return new NotificationLookup(stamp, started, Stopwatch.GetTimestamp());
    }

    private async Task DrainNotificationQueueAsync()
    {
        // One worker lookup at a time. This flag spans awaits and sessions; an old lookup
        // can finish harmlessly without allowing an unbounded set of simultaneous readers.
        if (_processingNotifications || _priming || !_running || _exiting) return;
        _processingNotifications = true;
        try
        {
            while (_running && !_priming && !_exiting)
            {
                if (_notificationQueue.Count == 0)
                {
                    if (!_queueRecoveryRequested) break;
                    _queueRecoveryRequested = false;
                    int recoveryEpoch = Volatile.Read(ref _sessionEpoch);
                    bool snapshotRead = false;
                    try
                    {
                        var snapshot = await ReadOutlookStampsAsync();
                        snapshotRead = true;
                        if (!IsCurrentSession(recoveryEpoch) || !_running) continue;
                        int added = 0;
                        foreach (var stamp in snapshot)
                            if (_gate!.TryAcceptAdded(stamp)) added++;
                        if (added > 0) ShowAlert(added, false);
                        Log("event_queue_recovery;outlook_notifications_added=" + added);
                    }
                    catch (Exception ex)
                    {
                        if (IsCurrentSession(recoveryEpoch))
                        {
                            if (snapshotRead) HandleAlertDisplayFailure(ex);
                            else HandleNotificationFailure("queue_recovery", ex);
                        }
                    }
                    continue;
                }
                var pending = _notificationQueue.Dequeue();
                _pendingNotificationIds.Remove(pending.Id);
                if (!IsCurrentSession(pending.Epoch)) continue;
                var gate = _gate!;
                bool lookupRead = false;
                try
                {
                    var lookup = await Task.Run(() => LookupNotification(pending, gate));
                    lookupRead = true;
                    long resumed = Stopwatch.GetTimestamp();
                    if (!IsCurrentSession(pending.Epoch) || !_running || lookup.Stamp is not NotificationStamp stamp) continue;
                    if (!gate.TryAcceptAdded(stamp)) continue;
                    long showing = Stopwatch.GetTimestamp();
                    ShowAlert(1, false);
                    long shown = Stopwatch.GetTimestamp();
                    SetStatus("Monitoring Outlook", "Alert displayed.");
                    // This is platform notification age, NOT native-toast-visible latency.
                    long creationAgeMs = Math.Max(0, (pending.CallbackUtc.UtcDateTime.Ticks - stamp.CreationUtcTicks) / TimeSpan.TicksPerMillisecond);
                    string timing = "outlook_notification_added" +
                        ";callback_to_lookup_ms=" + ElapsedMilliseconds(pending.CallbackTimestamp, lookup.StartedTimestamp) +
                        ";lookup_ms=" + ElapsedMilliseconds(lookup.StartedTimestamp, lookup.CompletedTimestamp) +
                        ";ui_queue_ms=" + ElapsedMilliseconds(lookup.CompletedTimestamp, resumed) +
                        ";popup_show_call_ms=" + ElapsedMilliseconds(showing, shown) +
                        ";callback_to_popup_call_ms=" + ElapsedMilliseconds(pending.CallbackTimestamp, shown) +
                        ";creation_to_callback_ms_approx=" + creationAgeMs;
                    // Background-priority logging lets the first render run before disk I/O.
                    _ = Dispatcher.InvokeAsync(() => Log(timing), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    if (IsCurrentSession(pending.Epoch))
                    {
                        if (lookupRead) HandleAlertDisplayFailure(ex);
                        else HandleNotificationFailure("notification_lookup", ex);
                    }
                }
            }
        }
        finally
        {
            _processingNotifications = false;
            if (_running && !_priming && !_exiting && (_notificationQueue.Count > 0 || _queueRecoveryRequested))
                _ = DrainNotificationQueueAsync();
        }
    }

    private static long ElapsedMilliseconds(long started, long finished) =>
        Math.Max(0, (long)Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds);

    private void HandleAlertDisplayFailure(Exception ex)
    {
        LogError("popup", ex);
        try { _popups.CloseAll(); }
        catch (Exception closeError) { LogError("popup_cleanup", closeError); }
        StopMonitoring("Alert display failed", "Windows could not show the alert on every display. Error code: " + Code(ex) + ". Open settings and enable monitoring again.");
        ShowSettings();
    }

    private void HandleNotificationFailure(string stage, Exception ex)
    {
        LogError(stage, ex);
        // A disappearing/broken individual notification must not shut down healthy
        // monitoring. Permission denial is different and needs a visible failure state.
        bool denied = ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005);
        try
        {
            denied |= _listener == null || _listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed;
        }
        catch (Exception healthError) { LogError("permission_health", healthError); }
        if (denied)
        {
            StopMonitoring("Notification access was turned off", "Allow notification access in Windows Settings, then enable monitoring here.");
            ShowSettings();
        }
    }

    private void StopMonitoring(string status, string detail)
    {
        Interlocked.Increment(ref _sessionEpoch);
        ClearCallbackIngress();
        _running = false;
        _priming = false;
        _notificationQueue.Clear();
        _pendingNotificationIds.Clear();
        _queueRecoveryRequested = false;
        if (_listener != null && _subscribed && _notificationHandler != null)
        {
            try { _listener.NotificationChanged -= _notificationHandler; } catch { }
            _subscribed = false;
        }
        _notificationHandler = null;
        // A runtime failure changes current readiness, not the saved request to
        // monitor. Retry that preference at the next launch without re-prompting.
        SetStatus(status, detail);
    }

    private void SetStatus(string status, string detail)
    {
        _status = status; _detail = detail;
        if (_statusLabel != null) _statusLabel.Text = status;
        if (_detailLabel != null) _detailLabel.Text = detail;
        if (_tray != null) _tray.Text = _running ? "Outlook Alert: monitoring Outlook" : "Outlook Alert: setup needed";
    }

    private async Task EnableStartupAsync()
    {
        _settings.StartAtSignIn = true;
        SaveSettings();
        await ChangeStartupAsync(true);
    }

    private async Task DisableStartupAsync()
    {
        _settings.StartAtSignIn = false;
        SaveSettings();
        await ChangeStartupAsync(false);
    }

    private async Task ChangeStartupAsync(bool enabled)
    {
        try
        {
            var state = await _startupRegistration.SetDesiredAsync(enabled);
            ApplyStartupState(state);
            Log("startup_request;desired=" + enabled + ";state=" + state);
        }
        catch (Exception ex)
        {
            LogError("startup_change", ex);
            ApplyStartupState(StartupRegistrationState.Unavailable);
        }
    }

    private async Task RefreshStartupStatusAsync()
    {
        try { ApplyStartupState(await _startupRegistration.GetStateAsync()); }
        catch (Exception ex)
        {
            LogError("startup_status", ex);
            ApplyStartupState(StartupRegistrationState.Unavailable);
        }
    }

    private void ApplyStartupState(StartupRegistrationState state)
    {
        if (_startupLabel != null) _startupLabel.Text = StartupPolicy.Describe(state);
        if (_startupEnableButton != null) _startupEnableButton.IsEnabled = state == StartupRegistrationState.Disabled;
        if (_startupDisableButton != null) _startupDisableButton.IsEnabled = state == StartupRegistrationState.Enabled;
    }

    private void ShowBackgroundSetupNotice()
    {
        _tray?.ShowBalloonTip(8000, "Outlook Alert needs setup", "Open Outlook Alert from the tray to enable monitoring or check its status.", Forms.ToolTipIcon.Warning);
    }

    private static void OpenWindowsStartupSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch { System.Windows.MessageBox.Show("Open Windows Settings > Apps > Startup, then switch Outlook Alert on.", "Outlook Alert"); }
    }

    private void CheckPermissionHealth()
    {
        if (!_running || _listener == null) return;
        try
        {
            if (_listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                StopMonitoring("Notification access was turned off", "Allow notification access in Windows Settings, then enable monitoring here.");
        }
        catch (Exception ex)
        {
            LogError("permission_health", ex);
            StopMonitoring("Monitoring stopped", "Windows notification access could not be checked. Enable monitoring again.");
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            CheckPermissionHealth();
            _ = RefreshStartupStatusAsync();
            _settingsWindow.Show(); _settingsWindow.WindowState = WindowState.Normal; _settingsWindow.Activate();
            return;
        }
        double width = Math.Min(680, SystemParameters.WorkArea.Width - 32);
        double height = Math.Min(740, SystemParameters.WorkArea.Height - 32);
        var window = new Window
        {
            Title = "Outlook Alert", Width = width, Height = height,
            MinWidth = Math.Min(480, width), MinHeight = Math.Min(400, height),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brush("#F5F8FC"), FontFamily = new FontFamily("Segoe UI")
        };
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = "Outlook Alert", FontSize = 32, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1C1C1E") });
        panel.Children.Add(new TextBlock { Text = "Stay informed across every display.", FontSize = 17, Foreground = Brush("#667085"), Margin = new Thickness(0, 6, 0, 24) });
        _statusLabel = new TextBlock { Text = _status, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1C1C1E"), TextWrapping = TextWrapping.Wrap };
        _detailLabel = new TextBlock { Text = _detail, FontSize = 15, Foreground = Brush("#636366"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18) };
        var statusPanel = new StackPanel { Margin = new Thickness(20) };
        statusPanel.Children.Add(_statusLabel); statusPanel.Children.Add(_detailLabel);
        panel.Children.Add(new Border { Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(18), Child = statusPanel, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock
        {
            Text = "Notification access is required. Only Outlook triggers alerts; message text is not read or saved.",
            FontSize = 14, Foreground = Brush("#48484A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose which accounts send alerts in Outlook notification settings.",
            FontSize = 14, Foreground = Brush("#48484A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20)
        });
        _enableButton = ActionButton("Enable monitoring");
        _enableButton.Click += async (_, _) => { await EnableMonitoringAsync(true); };
        panel.Children.Add(_enableButton);
        var test = ActionButton("Test alert on all screens", false);
        test.Click += (_, _) => ShowAlert(1, true);
        panel.Children.Add(test);
        panel.Children.Add(new TextBlock { Text = "Alert sound", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) });
        var soundChoice = new ComboBox
        {
            ItemsSource = AlertSoundCatalog.Choices, DisplayMemberPath = "Label", SelectedValuePath = "Id",
            SelectedValue = _settings.SoundId, FontSize = 16, Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        System.Windows.Automation.AutomationProperties.SetName(soundChoice, "Alert sound");
        soundChoice.SelectionChanged += (_, _) =>
        {
            _sounds.Stop();
            _settings.SoundId = AlertSoundCatalog.Normalize(soundChoice.SelectedValue as string);
            SaveSettings();
            _soundStatus = _settings.SoundId == AlertSoundCatalog.SilentId
                ? "Silent selected. Email alerts will still appear on all screens."
                : "Plays once for new email. Uses your Windows volume and output device.";
            if (_soundLabel != null) _soundLabel.Text = _soundStatus;
        };
        panel.Children.Add(soundChoice);
        var testSound = ActionButton("Test sound", false);
        testSound.Click += (_, _) => _sounds.Play(_settings.SoundId, preview: true);
        panel.Children.Add(testSound);
        _soundLabel = new TextBlock
        {
            Text = _settings.SoundId == AlertSoundCatalog.SilentId ? "Silent selected. Email alerts will still appear on all screens." : _soundStatus,
            FontSize = 13, Foreground = Brush("#636366"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20)
        };
        panel.Children.Add(_soundLabel);
        _startupEnableButton = ActionButton("Enable automatic startup", false);
        _startupEnableButton.Click += async (_, _) => { await EnableStartupAsync(); };
        panel.Children.Add(_startupEnableButton);
        _startupDisableButton = ActionButton("Disable automatic startup", false);
        _startupDisableButton.Click += async (_, _) => { await DisableStartupAsync(); };
        panel.Children.Add(_startupDisableButton);
        var windowsStartup = ActionButton("Open Windows startup settings", false);
        windowsStartup.Click += (_, _) => OpenWindowsStartupSettings();
        panel.Children.Add(windowsStartup);
        _startupLabel = new TextBlock { Text = "Checking startup status...", FontSize = 13, Foreground = Brush("#636366"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 16) };
        panel.Children.Add(_startupLabel);
        var logs = new Button { Content = "Open local diagnostic log", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) };
        logs.Click += (_, _) =>
        {
            Directory.CreateDirectory(StateDirectory);
            var path = Path.Combine(StateDirectory, "diagnostics.log");
            if (!File.Exists(path)) File.WriteAllText(path, "No diagnostic events recorded yet.\r\n");
            Process.Start(new ProcessStartInfo("notepad.exe", '"' + path + '"') { UseShellExecute = true });
        };
        panel.Children.Add(logs);
        panel.Children.Add(new TextBlock { Text = "The test checks the popup and selected sound. Send a new email to check detection.", FontSize = 13, Foreground = Brush("#636366"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Version 3.2.0  ·  Built for KYCDD  ·  facoding", FontSize = 12, Foreground = Brush("#7B8798"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) });
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Closing += (_, eventArgs) => { if (!_exiting) { eventArgs.Cancel = true; window.Hide(); } };
        _settingsWindow = window;
        window.Activated += (_, _) => { CheckPermissionHealth(); _ = RefreshStartupStatusAsync(); };
        window.Show(); window.Activate();
        _ = RefreshStartupStatusAsync();
    }

    private void ShowAlert(int added, bool preview)
    {
        _popups.Show(added, DateTimeOffset.Now, preview);
        _sounds.Play(_settings.SoundId, preview);
    }

    private static Button ActionButton(string title, bool primary = true) => new()
    {
        Content = title, FontSize = 16, FontWeight = FontWeights.SemiBold,
        Padding = new Thickness(18, 13, 18, 13), Margin = new Thickness(0, 0, 0, 10),
        Background = primary ? Brush("#007AFF") : System.Windows.Media.Brushes.White,
        Foreground = primary ? System.Windows.Media.Brushes.White : Brush("#0066CC"),
        BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
        Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(ActionButtonTemplate)
    };

    private const string ActionButtonTemplate = """
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
    <Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="12"
            Padding="{TemplateBinding Padding}" BorderThickness="2" BorderBrush="Transparent">
        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.88"/></Trigger>
        <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.75"/></Trigger>
        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#003D80"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.45"/></Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>
""";

    private static System.Windows.Media.Brush Brush(string color) => (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
    private static void OpenOutlook()
    {
        try { Process.Start(new ProcessStartInfo("ms-outlook:") { UseShellExecute = true }); }
        catch { System.Windows.MessageBox.Show("Open New Outlook from the Windows Start menu.", "Outlook Alert"); }
    }
    private static AlertSettings ReadSettings()
    {
        try { return JsonSerializer.Deserialize<AlertSettings>(File.ReadAllText(StatePath)) ?? new(); }
        catch { return new(); }
    }
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            string temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_settings));
            File.Move(temporary, StatePath, overwrite: true);
        }
        catch (Exception ex) { LogError("save_settings", ex); }
    }
    private static string Code(Exception ex) => "0x" + ex.HResult.ToString("X8");
    private static void LogError(string stage, Exception ex) => Log(stage + ";type=" + ex.GetType().Name + ";code=" + Code(ex));
    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            var path = Path.Combine(StateDirectory, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.WriteAllText(path, string.Empty);
            File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + ";" + line + Environment.NewLine);
        }
        catch { }
    }
    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        Interlocked.Increment(ref _sessionEpoch);
        ClearCallbackIngress();
        _running = false;
        _priming = false;
        _notificationQueue.Clear();
        _pendingNotificationIds.Clear();
        _queueRecoveryRequested = false;
        if (_listener != null && _subscribed && _notificationHandler != null) { try { _listener.NotificationChanged -= _notificationHandler; } catch { } }
        _notificationHandler = null;
        _popups.CloseAll();
        _sounds.Dispose();
        _showRegistration?.Unregister(null);
        _showSignal.Dispose();
        _tray?.Dispose();
        _settingsWindow?.Close();
        Shutdown();
    }
}
