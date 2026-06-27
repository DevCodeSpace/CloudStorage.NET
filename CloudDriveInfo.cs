namespace CloudStorage.Net
{
    public class CloudDriveInfo
    { 
        public string DriveName { get; set; }
        public string CloudDriveUserEmail { get; set; }
        public long? TotalSpace { get; set; }
        public long? UsedSpace { get; set; }
        public CloudDriveOptions CloudDriveOptions { get; set; }
    }
}
