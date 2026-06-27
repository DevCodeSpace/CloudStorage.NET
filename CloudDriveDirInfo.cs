using System;
using System.Collections.Generic;

namespace CloudStorage.Net
{
    public class CloudDriveDirInfo
    {
        public string DirId { get; set; }
        public string DirName { get; set; }
        public long? DirSize { get; set; }
        public string CloudDirPath { get; set; }
        public string CloudFileUrl { get; set; }
        public bool Status { get; set; }
        public bool Expand { get; set; }
        public bool IsProgressRunning { get; set; }
        public bool HasNoSubFolders { get; set; }
        public bool IsEnabled { get; set; }
        public string DisplayName { get; set; }
        public List<CloudDriveDirInfo> NavItems { get; set; } = new List<CloudDriveDirInfo>();
    }
}
