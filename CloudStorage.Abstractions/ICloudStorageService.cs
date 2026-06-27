using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudStorage.Abstractions
{
    public interface ICloudStorageService : IDisposable
    {
        string ProviderName { get; }
        
        // Logging
        Action<string, string> LogCallback { get; set; }

        // Auth & Connection
        Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);
        Task<bool> LogoutAsync(CancellationToken cancellationToken = default);
        Task<StorageSpaceInfo> GetStorageSpaceInfoAsync(CancellationToken cancellationToken = default);
        
        // Directory Operations
        Task<CloudDirectory> GetOrCreateDirectoryAsync(string directoryName, string parentDirectoryId = null, CancellationToken cancellationToken = default);
        Task<List<CloudDirectory>> GetSubDirectoriesAsync(string parentDirectoryId = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteDirectoryAsync(string directoryId, CancellationToken cancellationToken = default);
        
        // File Operations
        Task<List<CloudFile>> GetFilesAsync(string directoryId, CancellationToken cancellationToken = default);
        Task<bool> UploadFileAsync(string localFilePath, string destinationDirectoryId, CancellationToken cancellationToken = default);
        Task<bool> DownloadFileAsync(string fileId, string localDestinationPath, CancellationToken cancellationToken = default);
        Task<bool> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default);
        Task<bool> RenameFileAsync(string fileId, string newName, CancellationToken cancellationToken = default);
        
        // Share Support
        Task<string> GetShareLinkAsync(string fileId, CancellationToken cancellationToken = default);
    }
}
