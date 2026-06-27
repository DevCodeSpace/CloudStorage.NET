using System;

namespace CloudStorage.Abstractions
{
    public class StorageSpaceInfo
    {
        public string DriveName { get; set; }
        public string OwnerEmail { get; set; }
        public long? TotalSpace { get; set; }
        public long? UsedSpace { get; set; }
    }

    public abstract class CloudItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public long? Size { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public string Path { get; set; }
    }

    public class CloudFile : CloudItem
    {
        public string Description { get; set; }
    }

    public class CloudDirectory : CloudItem
    {
    }
}
