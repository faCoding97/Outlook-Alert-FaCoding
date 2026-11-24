namespace OutlookAlert.Setup
{
    // Release preparation replaces only these constants after package signing.
    // The installer refuses to run with placeholder or mismatched hashes.
    internal static class PayloadInfo
    {
        internal const string MsixSha256 = "2163DDE64A7390EA9AA32A1981CD8CBAE98C6CDB1FB817F266870D4E5BA318A3";
        internal const string CertificateSha256 = "63987BB0E5CB2B2FAC06F7BA51D7259E2CCF4692CA477F45C79FE13D3C7123EB";
        internal const string LauncherSha256 = "EC99300B061D558B88B7325546A0FB36E2DB2F2FBC522ADF1FC5DB0BBE403D2E";
        internal const string PackageName = "Faraz.OutlookAlert";
        internal const string ApplicationId = "App";
    }
}
