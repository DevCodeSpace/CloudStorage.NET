using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CloudStorage.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions;

namespace CloudStorage.OneDrive
{
    public class OneDriveStorageService : ICloudStorageService
    {
        private IPublicClientApplication _pca;
        private GraphServiceClient _graphClient;
        private string _driveId;
        private readonly string _clientId;
        private readonly string _cacheFilePath;
        private readonly string[] _scopes = new[] { "Files.ReadWrite.All", "User.Read" };

        public string ProviderName => "OneDrive";

        public Action<string, string> LogCallback { get; set; }

        public OneDriveStorageService(string clientId, string cacheFilePath = "onedrive_cache.bin")
        {
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            _cacheFilePath = cacheFilePath;
        }

        private void Log(string tag, string msg)
        {
            LogCallback?.Invoke(tag, msg);
        }

        public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Log("Auth", "Initializing MSAL Public Client Application...");
                _pca = PublicClientApplicationBuilder.Create(_clientId)
                    .WithRedirectUri("http://localhost")
                    .Build();

                // Setup persistent cache
                ConfigureSerialization(_pca.UserTokenCache);

                var authProvider = new MsalAuthenticationProvider(_pca, _scopes, LogCallback);
                _graphClient = new GraphServiceClient(authProvider);

                Log("Auth", "Verifying connection and fetching drive information...");
                var drive = await _graphClient.Me.Drive.GetAsync(cancellationToken: cancellationToken);
                _driveId = drive.Id;

                var user = await _graphClient.Me.GetAsync(cancellationToken: cancellationToken);
                Log("Auth", $"Successfully authenticated OneDrive user: {user.UserPrincipalName} ({user.DisplayName})");
                return true;
            }
            catch (Exception ex)
            {
                Log("Auth Error", ex.Message);
                return false;
            }
        }

        private void ConfigureSerialization(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccess(args =>
            {
                if (File.Exists(_cacheFilePath))
                {
                    try
                    {
                        args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(_cacheFilePath));
                    }
                    catch (Exception ex)
                    {
                        Log("Cache", $"Failed to deserialize cache: {ex.Message}");
                    }
                }
            });
            tokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged)
                {
                    try
                    {
                        File.WriteAllBytes(_cacheFilePath, args.TokenCache.SerializeMsalV3());
                    }
                    catch (Exception ex)
                    {
                        Log("Cache", $"Failed to serialize cache: {ex.Message}");
                    }
                }
            });
        }

        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
            _pca = null;
            _graphClient = null;
            _driveId = null;
            return Task.FromResult(true);
        }

        public async Task<StorageSpaceInfo> GetStorageSpaceInfoAsync(CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            var drive = await _graphClient.Drives[_driveId].GetAsync(cancellationToken: cancellationToken);
            var user = await _graphClient.Me.GetAsync(cancellationToken: cancellationToken);

            return new StorageSpaceInfo
            {
                DriveName = drive.Name ?? "OneDrive",
                OwnerEmail = user.UserPrincipalName ?? user.Mail,
                TotalSpace = drive.Quota?.Total,
                UsedSpace = drive.Quota?.Used
            };
        }

        public async Task<CloudDirectory> GetOrCreateDirectoryAsync(string directoryName, string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            var folderItem = new DriveItem
            {
                Name = directoryName,
                Folder = new Folder()
            };

            DriveItem result;
            if (string.IsNullOrEmpty(parentDirectoryId) || parentDirectoryId.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                result = await _graphClient.Drives[_driveId].Items["root"].Children.PostAsync(folderItem, cancellationToken: cancellationToken);
            }
            else
            {
                result = await _graphClient.Drives[_driveId].Items[parentDirectoryId].Children.PostAsync(folderItem, cancellationToken: cancellationToken);
            }

            return new CloudDirectory
            {
                Id = result.Id,
                Name = result.Name,
                Path = parentDirectoryId
            };
        }

        public async Task<List<CloudDirectory>> GetSubDirectoriesAsync(string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            var childrenRequest = (string.IsNullOrEmpty(parentDirectoryId) || parentDirectoryId.Equals("root", StringComparison.OrdinalIgnoreCase))
                ? _graphClient.Drives[_driveId].Items["root"].Children
                : _graphClient.Drives[_driveId].Items[parentDirectoryId].Children;

            var items = await childrenRequest.GetAsync(cancellationToken: cancellationToken);
            if (items?.Value == null) return new List<CloudDirectory>();

            return items.Value
                .Where(i => i.Folder != null)
                .Select(i => new CloudDirectory
                {
                    Id = i.Id,
                    Name = i.Name,
                    Path = parentDirectoryId
                }).ToList();
        }

        public async Task<bool> DeleteDirectoryAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            try
            {
                await _graphClient.Drives[_driveId].Items[directoryId].DeleteAsync(cancellationToken: cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                Log("DeleteDirectory Error", ex.Message);
                return false;
            }
        }

        public async Task<List<CloudFile>> GetFilesAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            var childrenRequest = (string.IsNullOrEmpty(directoryId) || directoryId.Equals("root", StringComparison.OrdinalIgnoreCase))
                ? _graphClient.Drives[_driveId].Items["root"].Children
                : _graphClient.Drives[_driveId].Items[directoryId].Children;

            var items = await childrenRequest.GetAsync(cancellationToken: cancellationToken);
            if (items?.Value == null) return new List<CloudFile>();

            return items.Value
                .Where(i => i.File != null)
                .Select(i => new CloudFile
                {
                    Id = i.Id,
                    Name = i.Name,
                    Size = i.Size,
                    ModifiedDate = i.LastModifiedDateTime?.DateTime,
                    Path = directoryId
                }).ToList();
        }

        public async Task<bool> UploadFileAsync(string localFilePath, string destinationDirectoryId, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            string fileName = Path.GetFileName(localFilePath);
            try
            {
                using (var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (string.IsNullOrEmpty(destinationDirectoryId) || destinationDirectoryId.Equals("root", StringComparison.OrdinalIgnoreCase))
                    {
                        await _graphClient.Drives[_driveId].Items["root"].ItemWithPath(fileName).Content.PutAsync(stream, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _graphClient.Drives[_driveId].Items[destinationDirectoryId].ItemWithPath(fileName).Content.PutAsync(stream, cancellationToken: cancellationToken);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("Upload Error", ex.Message);
                return false;
            }
        }

        public async Task<bool> DownloadFileAsync(string fileId, string localDestinationPath, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            try
            {
                var stream = await _graphClient.Drives[_driveId].Items[fileId].Content.GetAsync(cancellationToken: cancellationToken);
                
                string dir = Path.GetDirectoryName(localDestinationPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var fileStream = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("Download Error", ex.Message);
                return false;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            try
            {
                await _graphClient.Drives[_driveId].Items[fileId].DeleteAsync(cancellationToken: cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                Log("DeleteFile Error", ex.Message);
                return false;
            }
        }

        public async Task<bool> RenameFileAsync(string fileId, string newName, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            try
            {
                var updateItem = new DriveItem
                {
                    Name = newName
                };
                await _graphClient.Drives[_driveId].Items[fileId].PatchAsync(updateItem, cancellationToken: cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                Log("Rename Error", ex.Message);
                return false;
            }
        }

        public async Task<string> GetShareLinkAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (_graphClient == null || string.IsNullOrEmpty(_driveId)) throw new InvalidOperationException("Client not authenticated.");

            try
            {
                var body = new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
                {
                    Type = "view",
                    Scope = "anonymous"
                };

                var permission = await _graphClient.Drives[_driveId].Items[fileId].CreateLink.PostAsync(body, cancellationToken: cancellationToken);
                return permission?.Link?.WebUrl;
            }
            catch (Exception ex)
            {
                Log("Share Error", ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            _pca = null;
            _graphClient = null;
        }
    }

    public class MsalAuthenticationProvider : IAuthenticationProvider
    {
        private readonly IPublicClientApplication _pca;
        private readonly string[] _scopes;
        private readonly Action<string, string> _logCallback;

        public MsalAuthenticationProvider(IPublicClientApplication pca, string[] scopes, Action<string, string> logCallback)
        {
            _pca = pca;
            _scopes = scopes;
            _logCallback = logCallback;
        }

        public async Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object> additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
        {
            var accounts = await _pca.GetAccountsAsync();
            AuthenticationResult result;
            try
            {
                result = await _pca.AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                    .ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                _logCallback?.Invoke("Auth", "Silent login failed. Launching interactive browser authentication...");
                result = await _pca.AcquireTokenInteractive(_scopes)
                    .ExecuteAsync(cancellationToken);
            }

            request.Headers.Add("Authorization", $"Bearer {result.AccessToken}");
        }
    }
}
