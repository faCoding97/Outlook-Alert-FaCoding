namespace OutlookAlert.Setup
{
    // Release preparation replaces only these constants after package signing.
    // The installer refuses to run with placeholder or mismatched hashes.
    internal static class PayloadInfo
    {
        internal const string MsixSha256 = "3D10964BE35252BC7DA39C598D214BE1330BB6D91E139DBC7850A75122B85D6A";
        internal const string CertificateSha256 = "63987BB0E5CB2B2FAC06F7BA51D7259E2CCF4692CA477F45C79FE13D3C7123EB";
        internal const string LauncherSha256 = "218A0A148965ACE3F8177DAB599B9A0F68C196AA6C2B3ECE0B87AFD94238BF54";
        internal const string PackageName = "Faraz.OutlookAlert";
        internal const string ApplicationId = "App";
    }
}
