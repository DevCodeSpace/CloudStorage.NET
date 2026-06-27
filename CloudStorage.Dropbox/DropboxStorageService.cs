using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CloudStorage.Abstractions;
using Dropbox.Api;
using Dropbox.Api.Files;
using Dropbox.Api.Users;
using Dropbox.Api.Sharing;
using Newtonsoft.Json;

namespace CloudStorage.Dropbox
{
    public class DropboxStorageService : ICloudStorageService
    {
        private DropboxClient _client;
        private readonly string _appKey;
        private readonly string _appSecret;
        private readonly string _tokenFilePath;
        private const string RedirectUri = "http://localhost:52475/authorize/";

        public string ProviderName => "Dropbox";

        public Action<string, string> LogCallback { get; set; }

        public DropboxStorageService(string appKey, string appSecret, string tokenFilePath = "dropbox_token.json")
        {
            _appKey = appKey ?? throw new ArgumentNullException(nameof(appKey));
            _appSecret = appSecret ?? throw new ArgumentNullException(nameof(appSecret));
            _tokenFilePath = tokenFilePath;
        }

        private void Log(string tag, string msg)
        {
            LogCallback?.Invoke(tag, msg);
        }

        public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. Try loading cached token info
                DropboxTokenInfo tokenInfo = null;
                if (File.Exists(_tokenFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_tokenFilePath);
                        tokenInfo = JsonConvert.DeserializeObject<DropboxTokenInfo>(json);
                    }
                    catch (Exception ex)
                    {
                        Log("Auth", $"Failed to read cached token: {ex.Message}");
                    }
                }

                // 2. If no valid cache, perform full browser login
                if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.RefreshToken))
                {
                    Log("Auth", "No cached tokens found. Launching browser for Dropbox authorization...");
                    tokenInfo = await PerformBrowserAuthAsync(cancellationToken);
                    if (tokenInfo == null)
                    {
                        Log("Auth", "Authentication failed or was cancelled by user.");
                        return false;
                    }
                    
                    // Save tokens
                    File.WriteAllText(_tokenFilePath, JsonConvert.SerializeObject(tokenInfo, Formatting.Indented));
                    Log("Auth", $"Saved tokens to: {Path.GetFullPath(_tokenFilePath)}");
                }

                // 3. Initialize Dropbox client using auto-refresh
                Log("Auth", "Initializing Dropbox client with refresh support...");
                _client = new DropboxClient(
                    tokenInfo.AccessToken,
                    tokenInfo.RefreshToken,
                    _appKey,
                    _appSecret,
                    new DropboxClientConfig()
                );

                // Verify credential works
                var account = await _client.Users.GetCurrentAccountAsync();
                Log("Auth", $"Successfully authenticated Dropbox user: {account.Email} ({account.Name.DisplayName})");
                return true;
            }
            catch (Exception ex)
            {
                Log("Auth Error", ex.Message);
                return false;
            }
        }

        private async Task<DropboxTokenInfo> PerformBrowserAuthAsync(CancellationToken cancellationToken)
        {
            string state = Guid.NewGuid().ToString("N");

            // Get authorize URI
            var authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(
                OAuthResponseType.Code,
                _appKey,
                new Uri(RedirectUri),
                state: state,
                tokenAccessType: TokenAccessType.Offline // Request a refresh token
            );

            // Start HTTP loopback listener
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(RedirectUri);
                listener.Start();

                Log("Auth", $"Please open the browser authorization page if it doesn't open automatically...");
                OpenBrowser(authorizeUri.ToString());

                // Wait for redirect
                HttpListenerContext context;
                using (cancellationToken.Register(() => listener.Abort()))
                {
                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch (Exception)
                    {
                        Log("Auth", "Authentication listener was aborted or cancelled.");
                        return null;
                    }
                }

                // Process redirect query
                var request = context.Request;
                var queryState = request.QueryString["state"];
                var code = request.QueryString["code"];

                // Write success response to browser
                var response = context.Response;
                string responseHtml = "<html><body style='font-family: sans-serif; text-align: center; margin-top: 50px;'>" +
                                      "<h2>Authentication Successful!</h2>" +
                                      "<p>You may now close this tab and return to the Console Application.</p>" +
                                      "</body></html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
                response.ContentLength64 = buffer.Length;
                using (var output = response.OutputStream)
                {
                    await output.WriteAsync(buffer, 0, buffer.Length);
                }
                response.Close();

                if (queryState != state)
                {
                    throw new Exception("State validation failed. Request may have been intercepted.");
                }

                if (string.IsNullOrEmpty(code))
                {
                    throw new Exception("Authorization code was not returned by Dropbox.");
                }

                Log("Auth", "Exchanging authorization code for access and refresh tokens...");
                var tokenResult = await DropboxOAuth2Helper.ProcessCodeFlowAsync(
                    code,
                    _appKey,
                    _appSecret,
                    RedirectUri
                );

                return new DropboxTokenInfo
                {
                    AccessToken = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken
                };
            }
        }

        private void OpenBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(url);
            }
            catch
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    System.Diagnostics.Process.Start("xdg-open", url);
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    System.Diagnostics.Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
            if (File.Exists(_tokenFilePath))
            {
                File.Delete(_tokenFilePath);
            }
            return await Task.FromResult(true);
        }

        public async Task<StorageSpaceInfo> GetStorageSpaceInfoAsync(CancellationToken cancellationToken = default)
        {
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            var usage = await _client.Users.GetSpaceUsageAsync();
            var account = await _client.Users.GetCurrentAccountAsync();

            long total = 0;
            long used = (long)usage.Used;

            if (usage.Allocation.IsIndividual)
            {
                total = (long)usage.Allocation.AsIndividual.Value.Allocated;
            }
            else if (usage.Allocation.IsTeam)
            {
                total = (long)usage.Allocation.AsTeam.Value.Allocated;
            }

            return new StorageSpaceInfo
            {
                DriveName = "Dropbox Cloud Storage",
                OwnerEmail = account.Email,
                TotalSpace = total,
                UsedSpace = used
            };
        }

        private string NormalizePath(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath) || idOrPath.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            
            string path = idOrPath.Replace("\\", "/");
            if (!path.StartsWith("/"))
            {
                path = "/" + path;
            }
            return path;
        }

        public async Task<CloudDirectory> GetOrCreateDirectoryAsync(string directoryName, string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string parentPath = NormalizePath(parentDirectoryId);
            string fullPath = parentPath.TrimEnd('/') + "/" + directoryName;

            try
            {
                Log("CreateDirectory", $"Creating folder at path: {fullPath}");
                var result = await _client.Files.CreateFolderV2Async(fullPath);
                return new CloudDirectory
                {
                    Id = result.Metadata.PathDisplay,
                    Name = result.Metadata.Name,
                    Path = parentPath
                };
            }
            catch (ApiException<CreateFolderError> ex) when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsConflict)
            {
                Log("CreateDirectory", "Directory already exists, returning existing path metadata.");
                return new CloudDirectory
                {
                    Id = fullPath,
                    Name = directoryName,
                    Path = parentPath
                };
            }
        }

        public async Task<List<CloudDirectory>> GetSubDirectoriesAsync(string parentDirectoryId = null, CancellationToken cancellationToken = default)
        {
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string path = NormalizePath(parentDirectoryId);
            Log("ListDirectories", $"Listing contents of: {path}");
            
            var list = await _client.Files.ListFolderAsync(path);
            var dirs = list.Entries
                .Where(e => e.IsFolder)
                .Select(e => new CloudDirectory
                {
                    Id = e.PathDisplay,
                    Name = e.Name,
                    Path = path
                })
                .ToList();

            return dirs;
        }

        public async Task<bool> DeleteDirectoryAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string path = NormalizePath(directoryId);
            try
            {
                Log("DeleteDirectory", $"Deleting folder: {path}");
                await _client.Files.DeleteV2Async(path);
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
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string path = NormalizePath(directoryId);
            Log("ListFiles", $"Listing files in: {path}");

            var list = await _client.Files.ListFolderAsync(path);
            var files = list.Entries
                .Where(e => e.IsFile)
                .Select(e => e.AsFile)
                .Select(f => new CloudFile
                {
                    Id = f.PathDisplay,
                    Name = f.Name,
                    Size = (long)f.Size,
                    ModifiedDate = f.ClientModified,
                    Path = path
                })
                .ToList();

            return files;
        }

        public async Task<bool> UploadFileAsync(string localFilePath, string destinationDirectoryId, CancellationToken cancellationToken = default)
        {
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string destDir = NormalizePath(destinationDirectoryId);
            string destPath = destDir.TrimEnd('/') + "/" + Path.GetFileName(localFilePath);

            try
            {
                Log("Upload", $"Uploading local file {localFilePath} to Dropbox path: {destPath}");
                using (var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    await _client.Files.UploadAsync(destPath, WriteMode.Overwrite.Instance, body: stream);
                }
                Log("Upload", "Upload completed successfully.");
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
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string path = NormalizePath(fileId);
            try
            {
                Log("Download", $"Downloading file: {path}");
                var response = await _client.Files.DownloadAsync(path);
                
                string dir = Path.GetDirectoryName(localDestinationPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var responseStream = await response.GetContentAsStreamAsync())
                using (var fileStream = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await responseStream.CopyToAsync(fileStream);
                }

                Log("Download", $"Successfully downloaded file to: {localDestinationPath}");
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
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string path = NormalizePath(fileId);
            try
            {
                Log("DeleteFile", $"Deleting file: {path}");
                await _client.Files.DeleteV2Async(path);
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
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string sourcePath = NormalizePath(fileId);
            int lastSlash = sourcePath.LastIndexOf('/');
            string parentPath = lastSlash > -1 ? sourcePath.Substring(0, lastSlash) : "";
            string destPath = parentPath.TrimEnd('/') + "/" + newName;

            try
            {
                Log("Rename", $"Renaming {sourcePath} to {destPath}");
                await _client.Files.MoveV2Async(sourcePath, destPath);
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
            if (_client == null) throw new InvalidOperationException("Client not authenticated.");

            string path = NormalizePath(fileId);
            try
            {
                Log("Share", $"Creating shared link for: {path}");
                var link = await _client.Sharing.CreateSharedLinkWithSettingsAsync(path);
                return link.Url;
            }
            catch (ApiException<CreateSharedLinkWithSettingsError> ex) when (ex.ErrorResponse.IsSharedLinkAlreadyExists)
            {
                Log("Share", "Link already exists, retrieving existing links...");
                var links = await _client.Sharing.ListSharedLinksAsync(path, directOnly: true);
                var existing = links.Links.FirstOrDefault();
                return existing?.Url;
            }
            catch (Exception ex)
            {
                Log("Share Error", ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public class DropboxTokenInfo
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
}
