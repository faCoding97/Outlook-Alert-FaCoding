namespace OutlookAlert.Setup
{
    // Release preparation replaces only these constants after package signing.
    // The installer refuses to run with placeholder or mismatched hashes.
    internal static class PayloadInfo
    {
        internal const string MsixSha256 = "183F1B405D1BFD06223E076EC561B4DCEFB6CC96440CEE8CDBCB45F276786A49";
        internal const string CertificateSha256 = "30FAB459617FAEDE69DFFF1DF3FE8FA39708C03C2F322EEAEB240EED87387009";
        internal const string LauncherSha256 = "3FF20B116625E6518F4EF862B977743FC3877DD0C65A8B79674C8063089576F0";
        internal const string PackageName = "Faraz.OutlookAlert";
        internal const string ApplicationId = "App";
    }
}
