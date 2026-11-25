using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;

namespace Faraz.OutlookAlert;

// One player belongs to the app, not one per monitor. All clips are finite.
// SoundPlayer uses the Windows output device/mixer volume. No global volume is changed.
internal sealed class AlertSoundPlayer : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (SoundPlayer Player, Stream Stream)> _players = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AlertSoundGate _gate = new();
    private readonly Action<Exception>? _onError;
    private SoundPlayer? _current;
    private bool _disposed;

    public AlertSoundPlayer(Action<Exception>? onError = null)
    {
        _onError = onError;
        foreach (var choice in AlertSoundCatalog.Choices)
        {
            if (choice.Id == AlertSoundCatalog.SilentId) continue;
            Stream? stream = null;
            SoundPlayer? player = null;
            try
            {
                stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Faraz.OutlookAlert.Sounds." + choice.Id + ".wav")
                    ?? throw new FileNotFoundException("The packaged alert sound is missing: " + choice.Id);
                player = new SoundPlayer(stream);
                // Small local embedded clips are loaded once. Play() never blocks the UI
                // waiting for file/network IO and never falls back to the system beep.
                player.Load();
                _players.Add(choice.Id, (player, stream));
            }
            catch (Exception ex)
            {
                player?.Dispose();
                stream?.Dispose();
                Report(ex);
            }
        }
    }

    public bool Play(string? id, bool preview = false)
    {
        lock (_sync)
        {
            if (_disposed) return false;
            string selected = AlertSoundCatalog.Normalize(id);
            if (selected == AlertSoundCatalog.SilentId)
            {
                StopCurrent();
                return false;
            }
            if (!_players.TryGetValue(selected, out var clip)) return false;
            if (!_gate.TryAccept(selected, _clock.Elapsed, preview)) return false;
            try
            {
                StopCurrent();
                _current = clip.Player;
                _current.Play(); // Asynchronous, one pass only.
                return true;
            }
            catch (Exception ex)
            {
                Report(ex); // Sound failure must never stop visual alerts/monitoring.
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
            if (!_disposed) StopCurrent();
    }

    private void StopCurrent()
    {
        try { _current?.Stop(); }
        catch (Exception ex) { Report(ex); }
        _current = null;
    }

    private void Report(Exception exception)
    {
        try { _onError?.Invoke(exception); }
        catch { } // Diagnostic callbacks cannot break notification handling.
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            StopCurrent();
            _disposed = true;
            foreach (var clip in _players.Values)
            {
                clip.Player.Dispose();
                clip.Stream.Dispose();
            }
            _players.Clear();
            _clock.Stop();
        }
    }
}
