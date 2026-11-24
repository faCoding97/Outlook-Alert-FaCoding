namespace OutlookAlert.Setup
{
    // Release preparation replaces only these constants after package signing.
    // The installer refuses to run with placeholder or mismatched hashes.
    internal static class PayloadInfo
    {
        internal const string MsixSha256 = "35389FBDD0D236569944ECDBAF49F5B46B913BE906C21396B3810C8E424E6E2B";
        internal const string CertificateSha256 = "63987BB0E5CB2B2FAC06F7BA51D7259E2CCF4692CA477F45C79FE13D3C7123EB";
        internal const string LauncherSha256 = "6A326456534CFD3CC4AF23D5BFC81AB0B92C34357989E78F47FEE92F287684F8";
        internal const string PackageName = "Faraz.OutlookAlert";
        internal const string ApplicationId = "App";
    }
}
