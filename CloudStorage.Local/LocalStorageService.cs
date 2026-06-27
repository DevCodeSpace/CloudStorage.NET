using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudStorage.Abstractions;

namespace CloudStorage.Local
{
    public class LocalStorageService : ICloudStorageService
    {
        private readonly string _rootDir;

        public string ProviderName => "Local System";

        public Action<string, string> LogCallback { get; set; }

        public LocalStorageService(string rootDirectoryPath)
        {
            _rootDir = Path.GetFullPath(rootDirectoryPath);
            if (!Directory.Exists(_rootDir))
            {
                Directory.CreateDirectory(_rootDir);
            }
        }

        private void Log(string tag, string msg)
        {
            LogCallback?.Invoke(tag, msg);
        }

        public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            Log("Auth", "Local storage authenticated instantly.");
            return Task.FromResult(true);
        }

        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<StorageSpaceInfo> GetStorageSpaceInfoAsync(CancellationToken cancellationToken = default)
        {
            var drive = new DriveInfo(Path.GetPathRoot(_rootDir));
            return Task.FromResult(new StorageSpaceInfo
            {
                DriveName = $"Local ({drive.Name})",
                OwnerEmail = Environment.UserName,
                TotalSpace = drive.TotalSize,
                UsedSpace = drive.TotalSize - drive.AvailableFreeSpace
            });
        }

        private string ResolvePath(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath) || idOrPath == "root")
            {
                return _rootDir;
            }
            if (Path.IsPathRooted(idOrPath))
            {
                return idOrPath;
            }
            return Path.Combine(_rootDir, idOrPath);
        }

        public Task<CloudDirectory> GetOrCreateDirectoryAsync(string directoryName, string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            string parentPath = ResolvePath(parentDirectoryId);
            string newPath = Path.Combine(parentPath, directoryName);
            Directory.CreateDirectory(newPath);
            return Task.FromResult(new CloudDirectory
            {
                Id = newPath,
                Name = directoryName,
                Path = parentPath
            });
        }

        public Task<List<CloudDirectory>> GetSubDirectoriesAsync(string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            string path = ResolvePath(parentDirectoryId);
            var dirs = Directory.GetDirectories(path).Select(d => new CloudDirectory
            {
                Id = d,
                Name = Path.GetFileName(d),
                Path = path
            }).ToList();
            return Task.FromResult(dirs);
        }

        public Task<bool> DeleteDirectoryAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            string path = ResolvePath(directoryId);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<List<CloudFile>> GetFilesAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            string path = ResolvePath(directoryId);
            var files = Directory.GetFiles(path).Select(f => new CloudFile
            {
                Id = f,
                Name = Path.GetFileName(f),
                Size = new FileInfo(f).Length,
                ModifiedDate = File.GetLastWriteTime(f),
                Path = path
            }).ToList();
            return Task.FromResult(files);
        }

        public Task<bool> UploadFileAsync(string localFilePath, string destinationDirectoryId, CancellationToken cancellationToken = default)
        {
            string destDir = ResolvePath(destinationDirectoryId);
            string destFile = Path.Combine(destDir, Path.GetFileName(localFilePath));
            File.Copy(localFilePath, destFile, overwrite: true);
            return Task.FromResult(true);
        }

        public Task<bool> DownloadFileAsync(string fileId, string localDestinationPath, CancellationToken cancellationToken = default)
        {
            string sourceFile = ResolvePath(fileId);
            File.Copy(sourceFile, localDestinationPath, overwrite: true);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            string file = ResolvePath(fileId);
            if (File.Exists(file))
            {
                File.Delete(file);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> RenameFileAsync(string fileId, string newName, CancellationToken cancellationToken = default)
        {
            string sourceFile = ResolvePath(fileId);
            string destFile = Path.Combine(Path.GetDirectoryName(sourceFile), newName);
            File.Move(sourceFile, destFile);
            return Task.FromResult(true);
        }

        public Task<string> GetShareLinkAsync(string fileId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Uri(ResolvePath(fileId)).AbsoluteUri);
        }

        public void Dispose()
        {
        }
    }
}
