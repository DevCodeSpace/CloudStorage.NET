namespace CloudStorage.Net
{
    public class GoogleDriveConfig
    {
        public string RootFolderName { get; set; } = "GoogleDriveApp";
        public string GoogleDriveAppName { get; set; } = "GoogleDriveApp";
        public string GoogleDriveTokenFile { get; set; } = "token.json";
        public string GoogleDriveMusic { get; set; } = "Downloads";
        public string ClientSecretPath { get; set; } = "google_drive_client_secret.json";
        
        public bool ForceLogout { get; set; }
    }
}
