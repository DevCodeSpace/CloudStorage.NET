using System;

namespace CloudStorage.Net
{
    public class CloudDriveFileInfo
    {
        public string FileId { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        public string CloudFolderUrl { get; set; }
        public string Description { get; set; }
        public DateTime? CreatedDate { get; set; }
        public bool Status { get; set; }
    }
}
