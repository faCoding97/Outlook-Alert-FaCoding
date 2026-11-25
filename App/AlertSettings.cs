namespace Faraz.OutlookAlert;

internal sealed class AlertSettings
{
    public bool MonitoringEnabled { get; set; }
    public bool StartAtSignIn { get; set; } = true;
    public string SoundId { get; set; } = AlertSoundCatalog.DefaultId;
}
