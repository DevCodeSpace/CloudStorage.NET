using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using Dropbox.Api.Sharing;

namespace CloudStorage.Net
{
    public class DropboxManager : ICloudDrive
    {
        private readonly DropboxConfig _config;
        private readonly string _MusicDirName;
        private string _MusicDirId = string.Empty;
        private DropboxClient _DbClient;

        private readonly string ApiKey;
        private readonly string ApiSecret;
        private readonly string[] AppScopes = { 
            "account_info.read", 
            "files.metadata.read", 
            "files.metadata.write", 
            "files.content.write", 
            "files.content.read", 
            "sharing.write", 
            "sharing.read", 
            "file_requests.write", 
            "file_requests.read" 
        };

        private readonly List<string> LoopbackHosts = new List<string> {
            "http://localhost:59475/",
            "http://localhost:33314/",
            "http://localhost:49435/",
            "http://localhost:29444/",
            "http://localhost:44227/"
        };

        private int urlIndex = -1;
        private bool shouldIncreamentUrlIndex = false;
        private Uri RedirectUri = null;
        private Uri JSRedirectUri = null;

        public CloudDriveOptions CloudDriveType => CloudDriveOptions.DropBox;
        public string CloudDriveMusicDownloadDir => _config.DropboxMusic;
        public string CloudDriveBackupDirName => _config.DropboxAppName;
        public string CloudUserEmail { get; set; }

        public CancellationTokenSource _CancelTokenSource { get; } = new CancellationTokenSource();
        public CancellationToken _CancelToken => _CancelTokenSource.Token;

        // Callbacks to communicate with host app
        public Func<string> GetAccessTokenCallback { get; set; }
        public Func<string> GetUidCallback { get; set; }
        public Action<string, string> SaveTokenCallback { get; set; }
        public Action<string, string> LogCallback { get; set; }

        public DropboxManager(DropboxConfig config = null)
        {
            _config = config ?? new DropboxConfig();
            _MusicDirName = _config.RootFolderName;
            ApiKey = _config.ApiKey;
            ApiSecret = _config.ApiSecret;
            obtainValidLoopbackHost();
        }

        private void obtainValidLoopbackHost()
        {
            urlIndex++;
            if (urlIndex < LoopbackHosts.Count)
            {
                RedirectUri = new Uri(LoopbackHosts[urlIndex] + "authorize");
                JSRedirectUri = new Uri(LoopbackHosts[urlIndex] + "token");
            }
        }

        private void Log(string tag, string message)
        {
            LogCallback?.Invoke(tag, message);
            Debug.WriteLine($"[{tag}] {message}");
        }

        public async Task<bool> AuthenticateDrive()
        {
            bool res = false;
            try
            {
            StartAgainWithNextAvailableUrl:
                Log("DropboxRun", "1");
                Log("DropboxRun", "2");
                var accessToken = await GetAccessToken();

                if (shouldIncreamentUrlIndex)
                {
                    shouldIncreamentUrlIndex = false;
                    obtainValidLoopbackHost();
                    goto StartAgainWithNextAvailableUrl;
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    return false;
                }

                Log("DropboxRun", "3");
                var httpClient = new HttpClient()
                {
                    Timeout = TimeSpan.FromMinutes(20)
                };

                Log("DropboxRun", "4");
                var config = new DropboxClientConfig(_config.DropboxAppName)
                {
                    HttpClient = httpClient
                };

                _DbClient = new DropboxClient(accessToken, config);
                
                // Get Email address
                try
                {
                    var full = await _DbClient.Users.GetCurrentAccountAsync();
                    CloudUserEmail = full.Email;
                }
                catch (Exception ex)
                {
                    Log("GetCurrentAccountAsync ex", ex.Message);
                }

                res = true;
            }
            catch (Exception ex)
            {
                Log("AuthenticateDrive ex", ex.Message);
            }
            return res;
        }

        public async Task<bool> AuthenticateOnly()
        {
            return await AuthenticateDrive();
        }

        public async Task<bool> RefreshToken()
        {
            try
            {
                if (_DbClient != null)
                {
                    await _DbClient.RefreshAccessToken(AppScopes);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("RefreshToken ex", ex.Message);
            }
            return false;
        }

        private async Task<string> GetAccessToken()
        {
            Log("GetAccessToken", "21");
            var accessToken = GetAccessTokenCallback?.Invoke() ?? "";

            Log("GetAccessToken", "22");
            if (string.IsNullOrEmpty(accessToken))
            {
                Log("GetAccessToken", "23");
                try
                {
                    Log("GetAccessToken", "24");
                    var state = Guid.NewGuid().ToString("N");
                    var authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(OAuthResponseType.Token, ApiKey, RedirectUri, state: state);

                    var http = new HttpListener();
                    http.Prefixes.Add(LoopbackHosts[urlIndex]);

                    Log("GetAccessToken", "25");
                    http.Start();

                    OpenBrowser(authorizeUri.ToString());

                    Log("GetAccessToken", "26");
                    await HandleOAuth2Redirect(http);

                    Log("GetAccessToken", "27");
                    var result = await HandleJSRedirect(http);
                    var tokenResult = DropboxOAuth2Helper.ParseTokenFragment(result);

                    Log("GetAccessToken", "28");
                    if (tokenResult.State != state)
                    {
                        http.Close();
                        return null;
                    }

                    Log("GetAccessToken", "29");
                    accessToken = tokenResult.AccessToken;
                    var uid = tokenResult.Uid;
                    Log("GetAccessToken token", accessToken);

                    SaveTokenCallback?.Invoke(accessToken, uid);

                    http.Close();
                    Log("GetAccessToken", "32");
                }
                catch (Exception e)
                {
                    shouldIncreamentUrlIndex = true;
                    Log("GetAccessTokenex", e.Message);
                    Log("Re-attempt", "Attempting again with next registered url.");
                }
            }

            return accessToken;
        }

        private async Task HandleOAuth2Redirect(HttpListener http)
        {
            var context = await http.GetContextAsync();
            while (context.Request.Url.AbsolutePath != RedirectUri.AbsolutePath)
            {
                context = await http.GetContextAsync();
            }

            context.Response.ContentType = "text/html";
            byte[] buffer = Encoding.UTF8.GetBytes(RedirectHtml);
            context.Response.ContentLength64 = buffer.Length;
            using (var output = context.Response.OutputStream)
            {
                await output.WriteAsync(buffer, 0, buffer.Length);
            }
            context.Response.OutputStream.Close();
        }

        private async Task<Uri> HandleJSRedirect(HttpListener http)
        {
            var context = await http.GetContextAsync();
            while (context.Request.Url.AbsolutePath != JSRedirectUri.AbsolutePath)
            {
                context = await http.GetContextAsync();
            }

            var redirectUri = new Uri(context.Request.QueryString["url_with_fragment"]);
            return redirectUri;
        }

        private void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task<CloudDriveInfo> GetDriveInfo()
        {
            try
            {
                var usages = await _DbClient.Users.GetSpaceUsageAsync();
                if (usages != null)
                {
                    var driveInfo = new CloudDriveInfo();
                    driveInfo.DriveName = "Dropbox";
                    driveInfo.UsedSpace = (long)usages.Used;
                    driveInfo.TotalSpace = 0;
                    if (usages.Allocation.IsIndividual)
                    {
                        driveInfo.TotalSpace = (long)usages.Allocation.AsIndividual.Value.Allocated;
                    }
                    else if (usages.Allocation.IsTeam)
                    {
                        driveInfo.TotalSpace = (long)usages.Allocation.AsTeam.Value.Allocated;
                    }
                    driveInfo.CloudDriveUserEmail = CloudUserEmail;
                    return driveInfo;
                }
            }
            catch (Exception ex)
            {
                Log("GetDriveInfo ex", ex.Message);
            }
            return null;
        }

        public async Task<string> GetSongShareLink(string cloudFileId)
        {
            if (string.IsNullOrEmpty(cloudFileId)) return null;
            return await GenerateSharedLink(cloudFileId);
        }

        public async Task<List<CloudDriveFileInfo>> GetFiles(CloudDriveDirInfo folder, bool onlyRetrieveFirstFile)
        {
            List<CloudDriveFileInfo> files = null;
            try
            {
                var allFiles = await _DbClient.Files.ListFolderAsync(folder.CloudDirPath);
                var filesOfDir = allFiles.Entries.Where(e => e.IsFile);
                if (filesOfDir != null)
                {
                    files = new List<CloudDriveFileInfo>();
                    foreach (var metadata in filesOfDir)
                    {
                        var file = metadata.AsFile;
                        files.Add(new CloudDriveFileInfo
                        {
                            FileName = file.Name,
                            FileSize = (long)file.Size,
                            FileId = file.Id,
                            CloudFolderUrl = folder.CloudDirPath
                        });
                        if (onlyRetrieveFirstFile)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GetFiles ex", ex.Message);
            }
            return files;
        }

        public async Task<List<CloudDriveDirInfo>> GetDriveFolders(bool onlyRetrieveAppCloudFolder)
        {
            List<CloudDriveDirInfo> folders = null;
            try
            {
                var allFilesFolders = await _DbClient.Files.ListFolderAsync(string.Empty);
                var driveFolders = allFilesFolders.Entries.Where(i => i.IsFolder);
                if (driveFolders != null)
                {
                    folders = new List<CloudDriveDirInfo>();
                    foreach (var metadata in driveFolders)
                    {
                        var folder = metadata.AsFolder;
                        if (onlyRetrieveAppCloudFolder)
                        {
                            if (folder.Name.Equals(_MusicDirName, StringComparison.OrdinalIgnoreCase))
                            {
                                _MusicDirId = folder.Id;
                                folders.Add(new CloudDriveDirInfo
                                {
                                    DirName = folder.Name,
                                    CloudDirPath = metadata.PathLower,
                                    DirId = folder.Id
                                });
                                break;
                            }
                        }
                        else
                        {
                            if (!folder.Name.Equals(_MusicDirName, StringComparison.OrdinalIgnoreCase))
                            {
                                folders.Add(new CloudDriveDirInfo
                                {
                                    DirName = folder.Name,
                                    CloudDirPath = metadata.PathLower,
                                    DirId = folder.Id
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GetDriveFolders ex", ex.Message);
            }
            return folders;
        }

        public async Task<bool> Logout()
        {
            try
            {
                if (_DbClient != null)
                {
                    _DbClient.Dispose();
                    _DbClient = null;
                }
                SaveTokenCallback?.Invoke("", "");
                return true;
            }
            catch (Exception ex)
            {
                Log("Logout ex", ex.Message);
            }
            return false;
        }

        public async Task<bool> DownloadFile(CloudFileModel fileToDownload, string destinationFilePath)
        {
            bool success = false;
            const int bufferSize = 25 * 1024 * 1024;
            try
            {
                string downloadPath = fileToDownload.FileDownloadUrl + "/" + fileToDownload.FileName;
                if (!downloadPath.StartsWith("/"))
                {
                    downloadPath = "/" + downloadPath;
                }

                using (var response = await _DbClient.Files.DownloadAsync(downloadPath))
                using (var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write))
                {
                    var contentStream = await response.GetContentAsStreamAsync();
                    var buffer = new byte[bufferSize];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                    }
                }
                success = true;
            }
            catch (Exception ex)
            {
                Log("DownloadFile ex", ex.Message);
            }
            return success;
        }

        public async Task<bool> DeleteFilerequest(string filepathTodelete)
        {
            var res = false;
            try
            {
                string foldername = "";
                bool isDatabaseFile = Path.GetFileName(filepathTodelete).Equals("appdata.dat", StringComparison.OrdinalIgnoreCase);

                if (isDatabaseFile)
                {
                    foldername = await GetBackupfoldermetadata();
                }
                else
                {
                    foldername = await Getsongsfoldermetadata();
                }

                var path = filepathTodelete;
                if (!System.IO.File.Exists(path))
                {
                    var filename = System.IO.Path.GetFileName(path);
                    res = await DeleteDropboxFile(foldername, filename);
                }
            }
            catch (Exception ex)
            {
                Log("ex DeleteFilerequest", ex.Message);
            }
            return res;
        }

        private async Task<string> Getsongsfoldermetadata()
        {
            try
            {
                var list = await _DbClient.Files.ListFolderAsync("/" + _MusicDirName);
                var folder = list.Entries.FirstOrDefault(e => e.IsFolder && e.Name.Equals("songs", StringComparison.OrdinalIgnoreCase));
                if (folder != null)
                {
                    return folder.Name;
                }
            }
            catch (Exception ex)
            {
                Log("Getsongsfoldermetadata", ex.Message);
            }
            return "songs";
        }

        private async Task<string> GetBackupfoldermetadata()
        {
            try
            {
                var list = await _DbClient.Files.ListFolderAsync("/" + _MusicDirName);
                var folder = list.Entries.FirstOrDefault(e => e.IsFolder && e.Name.Equals("Backup", StringComparison.OrdinalIgnoreCase));
                if (folder != null)
                {
                    return folder.Name;
                }
            }
            catch (Exception ex)
            {
                Log("GetBackupfoldermetadata", ex.Message);
            }
            return "Backup";
        }

        private async Task<bool> DeleteDropboxFile(string folder, string fileName)
        {
            var isDeleted = false;
            try
            {
                await _DbClient.Files.DeleteV2Async("/" + _MusicDirName + "/" + folder + "/" + fileName);
                isDeleted = true;
            }
            catch (Exception ex)
            {
                Log("DeleteDropboxFile ex", ex.Message);
            }
            return isDeleted;
        }

        public async Task<bool> UploadFile(string filepathToUpload, string parentFolderIdToUpload)
        {
            var res = false;
            try
            {
                var path = filepathToUpload;
                if (System.IO.File.Exists(path))
                {
                    var fileName = System.IO.Path.GetFileName(path);
                    string destFolder = parentFolderIdToUpload;
                    if (!destFolder.StartsWith("/") && !destFolder.StartsWith("id:"))
                    {
                        destFolder = "/" + destFolder;
                    }

                    const int chunkSize = 150 * 1024 * 1024;
                    using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        var fileSize = fileStream.Length;
                        if (fileSize <= chunkSize)
                        {
                            var response = await _DbClient.Files.UploadAsync(destFolder + "/" + fileName, body: fileStream);
                            res = true;
                        }
                        else
                        {
                            var sessionId = string.Empty;
                            var chunkBuffer = new byte[chunkSize];
                            int bytesRead;

                            while ((bytesRead = fileStream.Read(chunkBuffer, 0, chunkSize)) > 0)
                            {
                                using (var memStream = new MemoryStream(chunkBuffer, 0, bytesRead))
                                {
                                    var uploadSessionCursor = sessionId == string.Empty
                                        ? new UploadSessionCursor(sessionId, 0)
                                        : new UploadSessionCursor(sessionId, (ulong)fileStream.Position - (ulong)bytesRead);

                                    if (sessionId == string.Empty)
                                    {
                                        var startResult = await _DbClient.Files.UploadSessionStartAsync(body: memStream);
                                        sessionId = startResult.SessionId;
                                    }
                                    else
                                    {
                                        await _DbClient.Files.UploadSessionAppendV2Async(uploadSessionCursor, body: memStream);
                                    }
                                }
                            }

                            var commitInfo = new CommitInfo(destFolder + "/" + fileName, WriteMode.Overwrite.Instance, false, DateTime.Now);
                            var finishResult = await _DbClient.Files.UploadSessionFinishAsync(
                                new UploadSessionCursor(sessionId, (ulong)fileStream.Position),
                                commitInfo,
                                body: new MemoryStream());

                            res = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("UploadFile ex", ex.Message);
            }
            return res;
        }

        public async Task<string> CreateFolder(string folderName, string ParentFolderId)
        {
            try
            {
                string path = "/" + folderName;
                try
                {
                    var existingMetadata = await _DbClient.Files.GetMetadataAsync(path);
                    if (existingMetadata != null && existingMetadata.IsFolder)
                    {
                        return existingMetadata.AsFolder.Id;
                    }
                }
                catch {}

                var folder = await _DbClient.Files.CreateFolderV2Async(path);
                _MusicDirId = folder.Metadata.Id;
                return folder.Metadata.Id;
            }
            catch (Exception ex)
            {
                Log("CreateFolder ex", ex.Message);
            }
            return null;
        }

        public async Task<string> CreatesubFolder(string folderName, string ParentFolderId)
        {
            try
            {
                string path = "/" + _MusicDirName + "/" + folderName;
                try
                {
                    var existingMetadata = await _DbClient.Files.GetMetadataAsync(path);
                    if (existingMetadata != null && existingMetadata.IsFolder)
                    {
                        return existingMetadata.AsFolder.Id;
                    }
                }
                catch {}

                var folder = await _DbClient.Files.CreateFolderV2Async(path);
                return folder.Metadata.Id;
            }
            catch (Exception ex)
            {
                Log("CreatesubFolder ex", ex.Message);
            }
            return null;
        }

        public async Task<bool> CreateCloudDriveRootFolder()
        {
            try
            {
                await _DbClient.Files.CreateFolderV2Async("/" + _MusicDirName + "/" + "songs");
            }
            catch {}
            try
            {
                await _DbClient.Files.CreateFolderV2Async("/" + _MusicDirName + "/" + "Backup");
            }
            catch {}
            return true;
        }

        public async Task<List<CloudDriveFileInfo>> GetFilesforid(CloudDriveDirInfo folder, bool onlyRetrieveFirstFile)
        {
            List<CloudDriveFileInfo> files = null;
            try
            {
                var path = folder.CloudDirPath + "/Songs";
                var allFiles = await _DbClient.Files.ListFolderAsync(path);
                var filesOfDir = allFiles.Entries.Where(e => e.IsFile);
                if (filesOfDir != null)
                {
                    files = new List<CloudDriveFileInfo>();
                    foreach (var metadata in filesOfDir)
                    {
                        var file = metadata.AsFile;
                        files.Add(new CloudDriveFileInfo
                        {
                            FileName = file.Name,
                            FileSize = (long)file.Size,
                            FileId = file.Id,
                            CloudFolderUrl = folder.CloudDirPath
                        });
                        if (onlyRetrieveFirstFile)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GetFilesforid ex", ex.Message);
            }
            return files;
        }

        public async Task<bool> DeleteFile(string fileId)
        {
            try
            {
                await _DbClient.Files.DeleteV2Async(fileId);
                return true;
            }
            catch (Exception ex)
            {
                Log("DeleteFile ex", ex.Message);
            }
            return false;
        }

        public async Task<bool> DeleteFolderById(string folderId)
        {
            try
            {
                await _DbClient.Files.DeleteV2Async(folderId);
                return true;
            }
            catch (Exception ex)
            {
                Log("DeleteFolderById ex", ex.Message);
            }
            return false;
        }

        public async Task<bool> ShareFile(string toUser, string cloudFileId)
        {
            try
            {
                var members = new[] { new MemberSelector.Email(toUser) };
                await _DbClient.Sharing.AddFileMemberAsync(cloudFileId, members);
                return true;
            }
            catch (Exception ex)
            {
                Log("ShareFile ex", ex.Message);
            }
            return false;
        }

        public async Task<bool> ChangeFilepermission(string toUser, string cloudFileId)
        {
            try
            {
                if (!string.IsNullOrEmpty(toUser))
                {
                    var emails = toUser.Split(',')
                                       .Select(e => e.Trim())
                                       .Where(e => !string.IsNullOrEmpty(e));
                    foreach (var email in emails)
                    {
                        await ShareFile(email, cloudFileId);
                    }
                }

                var url = await GenerateSharedLink(cloudFileId);
                return !string.IsNullOrEmpty(url);
            }
            catch (Exception ex)
            {
                Log("ChangeFilepermission ex", ex.Message);
            }
            return false;
        }

        private async Task<string> GenerateSharedLink(string cloudFileId)
        {
            try
            {
                // List existing links first to avoid throwing exceptions
                var links = await _DbClient.Sharing.ListSharedLinksAsync(cloudFileId, directOnly: true);
                var existing = links.Links.FirstOrDefault();
                if (existing != null)
                {
                    return existing.Url;
                }

                var sharedLinkArg = new CreateSharedLinkWithSettingsArg(cloudFileId);
                var sharedLinkSettings = await _DbClient.Sharing.CreateSharedLinkWithSettingsAsync(sharedLinkArg);
                return sharedLinkSettings.Url;
            }
            catch (Exception ex)
            {
                Log("GenerateSharedLink ex", ex.Message);
                return null;
            }
        }

        public Task<bool> UnShareFile(string cloudFileId, string emailAddress)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> RenameCloudFile(string newName, string cloudFileId)
        {
            try
            {
                string sourcePath = cloudFileId;
                int lastSlash = sourcePath.LastIndexOf('/');
                string parentPath = lastSlash > -1 ? sourcePath.Substring(0, lastSlash) : "";
                string destPath = parentPath.TrimEnd('/') + "/" + newName;
                await _DbClient.Files.MoveV2Async(sourcePath, destPath);
                return true;
            }
            catch (Exception ex)
            {
                Log("RenameCloudFile Error", ex.Message);
                return false;
            }
        }

        public async Task<List<CloudDriveDirInfo>> GetSubFolder(CloudDriveDirInfo folder)
        {
            List<CloudDriveDirInfo> folders = null;
            try
            {
                var driveFolders = await _DbClient.Files.ListFolderAsync(folder.DirId);
                if (driveFolders != null)
                {
                    folders = new List<CloudDriveDirInfo>();
                    foreach (var metadata in driveFolders.Entries.Where(e => e.IsFolder))
                    {
                        var f = metadata.AsFolder;
                        folders.Add(new CloudDriveDirInfo
                        {
                            DirName = f.Name,
                            CloudDirPath = f.PathLower,
                            DirId = f.Id
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GetSubFolder ex", ex.Message);
            }
            return folders;
        }

        public async Task<string> GetLastBackupDate(CloudDriveDirInfo folder)
        {
            try
            {
                var path = "/" + _MusicDirName + "/data/backup.zip";
                var metadata = await _DbClient.Files.GetMetadataAsync(path);
                if (metadata.IsFile)
                {
                    return metadata.AsFile.ServerModified.ToString();
                }
            }
            catch (Exception ex)
            {
                Log("GetLastBackupDate ex", ex.Message);
            }
            return null;
        }

        public bool CancelTask()
        {
            try
            {
                _CancelTokenSource.Cancel();
                return true;
            }
            catch (Exception ex)
            {
                Log("CancelTask ex", ex.Message);
            }
            return false;
        }

        public void Dispose()
        {
            _DbClient?.Dispose();
        }

        private const string RedirectHtml = @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Authentication Successful</title>
    <script type=""text/javascript"">
        function redirect() {
            document.location.href = ""/token?url_with_fragment="" + encodeURIComponent(document.location.href);
        }
    </script>
</head>
<body onload=""redirect()"" style=""font-family: sans-serif; text-align: center; margin-top: 50px;"">
    <h2>Authentication Successful</h2>
    <p>You may close this tab and return to the application.</p>
</body>
</html>";
    }
}
