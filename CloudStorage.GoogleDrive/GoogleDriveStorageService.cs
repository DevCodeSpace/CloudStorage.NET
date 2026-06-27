using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudStorage.Abstractions;
using CloudStorage.Net;

namespace CloudStorage.GoogleDrive
{
    public class GoogleDriveStorageService : ICloudStorageService
    {
        private readonly GoogleDriveManager _manager;

        public string ProviderName => "Google Drive";

        public Action<string, string> LogCallback
        {
            get => _manager.LogCallback;
            set => _manager.LogCallback = value;
        }

        public GoogleDriveStorageService(GoogleDriveConfig config)
        {
            _manager = new GoogleDriveManager(config ?? new GoogleDriveConfig());
        }

        public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            // Note: CancellationToken is not directly supported by the underlying AuthenticateDrive,
            // but we can register cancellation to the manager's token source if needed.
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => _manager._CancelTokenSource.Cancel());
            }
            return await _manager.AuthenticateDrive();
        }

        public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
        {
            return await _manager.Logout();
        }

        public async Task<StorageSpaceInfo> GetStorageSpaceInfoAsync(CancellationToken cancellationToken = default)
        {
            var info = await _manager.GetDriveInfo();
            if (info == null) return null;

            return new StorageSpaceInfo
            {
                DriveName = info.DriveName,
                OwnerEmail = info.CloudDriveUserEmail,
                TotalSpace = info.TotalSpace,
                UsedSpace = info.UsedSpace
            };
        }

        public async Task<CloudDirectory> GetOrCreateDirectoryAsync(string directoryName, string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            string id = await _manager.CreateFolder(directoryName, parentDirectoryId);
            if (string.IsNullOrEmpty(id)) return null;

            return new CloudDirectory
            {
                Id = id,
                Name = directoryName,
                Path = parentDirectoryId
            };
        }

        public async Task<List<CloudDirectory>> GetSubDirectoriesAsync(string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            var folder = new CloudDriveDirInfo { DirId = parentDirectoryId ?? "root" };
            var subfolders = await _manager.GetSubFolder(folder);
            if (subfolders == null) return new List<CloudDirectory>();

            return subfolders.Select(f => new CloudDirectory
            {
                Id = f.DirId,
                Name = f.DirName,
                Size = f.DirSize,
                Path = parentDirectoryId
            }).ToList();
        }

        public async Task<bool> DeleteDirectoryAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            return await _manager.DeleteFolderById(directoryId);
        }

        public async Task<List<CloudFile>> GetFilesAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            var folder = new CloudDriveDirInfo { DirId = directoryId ?? "root" };
            var files = await _manager.GetFiles(folder, onlyRetrieveFirstFile: false);
            if (files == null) return new List<CloudFile>();

            return files.Select(f => new CloudFile
            {
                Id = f.FileId,
                Name = f.FileName,
                Size = f.FileSize,
                ModifiedDate = f.CreatedDate,
                Description = f.Description,
                Path = directoryId
            }).ToList();
        }

        public async Task<bool> UploadFileAsync(string localFilePath, string destinationDirectoryId, CancellationToken cancellationToken = default)
        {
            return await _manager.UploadFile(localFilePath, destinationDirectoryId ?? "root");
        }

        public async Task<bool> DownloadFileAsync(string fileId, string localDestinationPath, CancellationToken cancellationToken = default)
        {
            var model = new CloudFileModel { FileId = fileId };
            return await _manager.DownloadFile(model, localDestinationPath);
        }

        public async Task<bool> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            return await _manager.DeleteFile(fileId);
        }

        public async Task<bool> RenameFileAsync(string fileId, string newName, CancellationToken cancellationToken = default)
        {
            return await _manager.RenameCloudFile(newName, fileId);
        }

        public async Task<string> GetShareLinkAsync(string fileId, CancellationToken cancellationToken = default)
        {
            return await _manager.GetSongShareLink(fileId);
        }

        public void Dispose()
        {
            _manager.Dispose();
        }
    }
}
