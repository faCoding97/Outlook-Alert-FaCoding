using System;
using System.Collections.Generic;

namespace Faraz.OutlookAlert;

// Deliberately contains no Windows UI, notification-content, or mailbox parsing APIs.
internal readonly record struct NotificationStamp(string ApplicationId, uint Id, long CreationUtcTicks);

internal sealed class NotificationGate
{
    private readonly HashSet<string> _allowedApplicationIds;
    private const int MaxRemembered = 4096;
    private HashSet<(uint Id, long CreationUtcTicks)> _known = new();
    private readonly Queue<(uint Id, long CreationUtcTicks)> _arrivalOrder = new();

    public NotificationGate(IEnumerable<string> allowedApplicationIds)
    {
        _allowedApplicationIds = new(allowedApplicationIds, StringComparer.OrdinalIgnoreCase);
    }

    public bool AcceptsApplication(string? applicationId) => applicationId != null && _allowedApplicationIds.Contains(applicationId);
    public int KnownCount => _known.Count;

    // Called on the UI dispatcher for one Added event. Never discards unrelated seen IDs.
    public bool TryAcceptAdded(NotificationStamp notification)
    {
        if (!AcceptsApplication(notification.ApplicationId)) return false;
        var key = (notification.Id, notification.CreationUtcTicks);
        if (!_known.Add(key)) return false;
        _arrivalOrder.Enqueue(key);
        TrimHistory();
        return true;
    }

    private void TrimHistory()
    {
        while (_arrivalOrder.Count > MaxRemembered)
            _known.Remove(_arrivalOrder.Dequeue());
    }

    public int ApplySnapshot(IEnumerable<NotificationStamp> notifications, bool baseline = false)
    {
        var current = new HashSet<(uint Id, long CreationUtcTicks)>();
        foreach (var notification in notifications)
        {
            if (AcceptsApplication(notification.ApplicationId))
                current.Add((notification.Id, notification.CreationUtcTicks));
        }
        int added = 0;
        if (!baseline)
            foreach (var stamp in current)
                if (!_known.Contains(stamp)) added++;
        // Initial/recovery snapshots replace the baseline; arrivals use the incremental path.
        _known = current;
        _arrivalOrder.Clear();
        foreach (var stamp in current) _arrivalOrder.Enqueue(stamp);
        TrimHistory();
        return added;
    }
}
