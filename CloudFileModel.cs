namespace CloudStorage.Net
{
    public class CloudFileModel
    {
        public string FileId { get; set; }
        public string FileName { get; set; }
        public string PathToSave { get; set; }
        public bool IsInstalled { get; set; }
        public string FileDownloadUrl { get; set; }
        public long AppSizeInBytes { get; set; }
        public string ItemTypeStr { get; set; }
        public string OperationType { get; set; }
        public bool IsSelected { get; set; }
        public int Status { get; set; }

        public CloudFileModel() { }

        public CloudFileModel(CloudDriveFileInfo driveFile)
        {
            this.FileName = driveFile.FileName;
            this.FileId = driveFile.FileId;
            this.FileDownloadUrl = driveFile.CloudFolderUrl;
        }
    }
}
