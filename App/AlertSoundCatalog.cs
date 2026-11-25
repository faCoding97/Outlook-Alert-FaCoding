using System;
using System.Collections.Generic;

namespace Faraz.OutlookAlert;

internal sealed record AlertSoundChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

internal static class AlertSoundCatalog
{
    public const string DefaultId = "chime";
    public const string SilentId = "silent";

    public static IReadOnlyList<AlertSoundChoice> Choices { get; } = Array.AsReadOnly(new[]
    {
        new AlertSoundChoice(DefaultId, "Chime"),
        new AlertSoundChoice("bell", "Bell"),
        new AlertSoundChoice("double-beep", "Double beep"),
        new AlertSoundChoice("soft-ping", "Soft ping"),
        new AlertSoundChoice("urgent", "Urgent"),
        new AlertSoundChoice(SilentId, "Silent")
    });

    public static string Normalize(string? id)
    {
        foreach (var choice in Choices)
            if (string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase)) return choice.Id;
        return DefaultId;
    }
}

// The host passes a monotonic elapsed time; clock changes cannot suppress audio.
internal sealed class AlertSoundGate
{
    private readonly TimeSpan _minimumGap;
    private TimeSpan? _lastAlert;

    public AlertSoundGate(TimeSpan? minimumGap = null)
    {
        _minimumGap = minimumGap ?? TimeSpan.FromSeconds(2);
        if (_minimumGap < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumGap));
    }

    public bool TryAccept(string? id, TimeSpan elapsed, bool preview = false)
    {
        if (AlertSoundCatalog.Normalize(id) == AlertSoundCatalog.SilentId) return false;
        if (preview) return true; // A preview never suppresses a real incoming email.
        if (_lastAlert is TimeSpan previous && elapsed - previous < _minimumGap) return false;
        _lastAlert = elapsed;
        return true;
    }
}
