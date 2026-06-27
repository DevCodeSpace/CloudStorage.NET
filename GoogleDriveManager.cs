using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using File = System.IO.File;
using GDrivePermission = Google.Apis.Drive.v3.Data.Permission;

namespace CloudStorage.Net
{
    public class GoogleDriveManager : ICloudDrive
    {
        private string _MusicDirName;
        private string _MusicDirId = string.Empty;

        private static readonly string[] Scopes =
        {
            "email",
            "https://www.googleapis.com/auth/drive.file"
        };

        private string ApplicationName;
        private static DriveService _DriveService;
        private static UserCredential _Credential;
        private static string _ClientId;
        private static string _ProjectId;
        private static string _ProjectNumber;

        public UserCredential Credential => _Credential;
        public string ClientId => _ClientId;
        public string ProjectId => _ProjectId;
        public string ProjectNumber => _ProjectNumber;

        public async Task<string> GetAccessToken()
        {
            if (_Credential != null)
            {
                if (_Credential.Token.IsExpired(Google.Apis.Util.SystemClock.Default))
                {
                    await _Credential.RefreshTokenAsync(CancellationToken.None);
                }
                return _Credential.Token.AccessToken;
            }
            return null;
        }

        private GoogleDriveConfig _config;
        public CloudDriveOptions CloudDriveType => CloudDriveOptions.GoogleDrive;

        public string CloudDriveMusicDownloadDir => _config.GoogleDriveMusic;
        public string CloudDriveBackupDirName => _config.GoogleDriveAppName;

        public CancellationTokenSource _CancelTokenSource { get; } = new CancellationTokenSource();
        public CancellationToken _CancelToken => _CancelTokenSource.Token;

        public string CloudUserEmail { get; set; }

        public Action<string, string> LogCallback { get; set; }

        public GoogleDriveManager(GoogleDriveConfig config = null)
        {
            _config = config ?? new GoogleDriveConfig();
            _MusicDirName = _config.RootFolderName;
            ApplicationName = _config.GoogleDriveAppName;
        }

        private void Log(string tag, string message)
        {
            LogCallback?.Invoke(tag, message);
            Debug.WriteLine($"[{tag}] {message}");
        }

        private static string GetMimeMapping(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            switch (extension)
            {
                case ".mp3": return "audio/mpeg";
                case ".wav": return "audio/wav";
                case ".m4a": return "audio/mp4";
                case ".json": return "application/json";
                case ".txt": return "text/plain";
                case ".xml": return "application/xml";
                case ".zip": return "application/zip";
                case ".db": case ".sqlite": return "application/x-sqlite3";
                default: return "application/octet-stream";
            }
        }

        public async Task<bool> AuthenticateDrive()
        {
            var res = false;
            try
            {
                bool islogin = _config.ForceLogout;
                if (!islogin)
                {
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    Log("AuthenticateDrive", "Initialize");
                }
                else
                {
                    ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
                }

                using (var stream = new FileStream(_config.ClientSecretPath, FileMode.Open, FileAccess.Read))
                {
                    string credPath = _config.GoogleDriveTokenFile;

                    var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
                    _ClientId = secrets.ClientId;
                    
                    if (!string.IsNullOrEmpty(_ClientId) && _ClientId.Contains("-"))
                    {
                        _ProjectNumber = _ClientId.Split('-')[0];
                    }

                    try
                    {
                        _ProjectId = string.Empty;
                        string fullContent = File.ReadAllText(_config.ClientSecretPath);
                        int firstBrace = fullContent.IndexOf('{');
                        if (firstBrace > -1)
                        {
                            var cleanJson = fullContent.Substring(firstBrace);
                            var obj = Newtonsoft.Json.Linq.JObject.Parse(cleanJson);
                            var installed = obj["installed"] ?? obj["web"];
                            _ProjectId = installed?["project_id"]?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("ParseProjectIdError", ex.Message);
                    }

                    _Credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true));
                    _config.ForceLogout = false;
                    Log("AuthenticateDrive", "Credential file saved to: " + credPath);
                }

                _DriveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = _Credential,
                    ApplicationName = ApplicationName,
                });

                Log("AuthenticateDrive", "Success");
                res = true;
            }
            catch (HttpRequestException httpEx)
            {
                Log("HttpRequestException", httpEx.Message);
            }
            catch (WebException webEx)
            {
                Log("WebException", webEx.Message);
            }
            catch (AuthenticationException authEx)
            {
                Log("AuthenticationException", authEx.Message);
            }
            catch (Exception ex)
            {
                Log("General Exception", ex.Message);
            }
            return res;
        }

        public async Task<bool> AuthenticateOnly()
        {
            var res = false;
            try
            {
                Log("AuthenticateDrive", "Initialize");

                using (var stream = new FileStream(_config.ClientSecretPath, FileMode.Open, FileAccess.Read))
                {
                    string credPath = _config.GoogleDriveTokenFile;

                    var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
                    _ClientId = secrets.ClientId;

                    if (!string.IsNullOrEmpty(_ClientId) && _ClientId.Contains("-"))
                    {
                        _ProjectNumber = _ClientId.Split('-')[0];
                    }

                    try
                    {
                        _ProjectId = string.Empty;
                        string fullContent = File.ReadAllText(_config.ClientSecretPath);
                        int firstBrace = fullContent.IndexOf('{');
                        if (firstBrace > -1)
                        {
                            var cleanJson = fullContent.Substring(firstBrace);
                            var obj = Newtonsoft.Json.Linq.JObject.Parse(cleanJson);
                            var installed = obj["installed"] ?? obj["web"];
                            _ProjectId = installed?["project_id"]?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("ParseProjectIdError", ex.Message);
                    }

                    _Credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true));

                    Log("AuthenticateDrive", "Credential file saved to: " + credPath);
                }

                Log("AuthenticateDrive", "Success");
                res = true;
            }
            catch (Exception ex)
            {
                Log("ex", ex.Message);
                Log("AuthenticateDrive", "Failed");
            }
            return res;
        }

        public async Task<CloudDriveInfo> GetDriveInfo()
        {
            try
            {
                Log("GetDriveInfo", "Getting drive storage info");
                AboutResource.GetRequest ag = new AboutResource.GetRequest(_DriveService);
                ag.Fields = "user, storageQuota";
                var response = await ag.ExecuteAsync();

                if (response != null)
                {
                    CloudDriveInfo driveInfo = new CloudDriveInfo();
                    driveInfo.DriveName = "Google Drive";
                    driveInfo.UsedSpace = response.StorageQuota.Usage;
                    driveInfo.TotalSpace = response.StorageQuota.Limit;

                    driveInfo.CloudDriveUserEmail = response.User?.EmailAddress;
                    CloudUserEmail = driveInfo.CloudDriveUserEmail;

                    driveInfo.CloudDriveOptions = CloudDriveOptions.GoogleDrive;

                    return driveInfo;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Log("ex GetDriveInfo", ex.Message);
            }

            return null;
        }

        public Task<string> GetSongShareLink(string cloudFileId)
        {
            string fileUrl = string.Empty;
            try
            {
                fileUrl = $"https://drive.google.com/file/d/{cloudFileId}/view";
            }
            catch (Exception ex)
            {
                Log("ex ShareFile", ex.Message);
            }
            return Task.FromResult(fileUrl);
        }

        public Task<List<CloudDriveFileInfo>> GetFiles(CloudDriveDirInfo folder, bool onlyRetrieveFirstFile)
        {
            return Task.Run<List<CloudDriveFileInfo>>(() =>
            {
                List<CloudDriveFileInfo> driveFiles = null;
                try
                {
                    var resultFileList = new List<Google.Apis.Drive.v3.Data.File>();

                    FilesResource.ListRequest request = _DriveService.Files.List();
                    request.Q = $"'{folder.DirId}' in parents";
                    request.Fields = "nextPageToken, files(id, name, trashed, webViewLink, description, size, createdTime)";

                    do
                    {
                        try
                        {
                            FileList files = request.Execute();
                            resultFileList.AddRange(files.Files.Where(i => i.Trashed == false));
                            request.PageToken = files.NextPageToken;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("An error occurred: " + e.Message);
                            request.PageToken = null;
                        }
                    } while (!String.IsNullOrEmpty(request.PageToken));


                    if (resultFileList != null)
                    {
                        if (resultFileList.Count > 0)
                        {
                            driveFiles = new List<CloudDriveFileInfo>();

                            foreach (var file in resultFileList)
                            {
                                if (onlyRetrieveFirstFile)
                                {
                                    driveFiles.Add(new CloudDriveFileInfo
                                    {
                                        FileId = file.Id,
                                        FileName = file.Name,
                                        FileSize = file.Size,
                                        Description = file.Description,
                                        CreatedDate = file.CreatedTime
                                    });
                                    break;
                                }
                                else
                                {
                                    driveFiles.Add(new CloudDriveFileInfo
                                    {
                                        FileId = file.Id,
                                        FileName = file.Name,
                                        FileSize = file.Size,
                                        Description = file.Description,
                                        CreatedDate = file.CreatedTime
                                    });
                                }
                            }
                        }
                        else
                        {
                            Log(" GetFiles", "No files found.");
                        }
                    }
                    else
                    {
                        Log(" GetFiles", "No files found.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    Log("ex GetFiles", ex.Message);
                }
                return driveFiles;
            }, _CancelToken);
        }

        public Task<List<CloudDriveDirInfo>> GetDriveFolders(bool onlyRetrieveAppCloudFolder)
        {
            return Task.Run<List<CloudDriveDirInfo>>(() =>
            {
                List<CloudDriveDirInfo> dirs = new List<CloudDriveDirInfo>();
                try
                {
                    if (_DriveService == null)
                        return null;

                    FilesResource.ListRequest listRequest = _DriveService.Files.List();
                    listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and 'root' in parents";
                    listRequest.PageSize = 100;
                    listRequest.Fields = "nextPageToken, files(id, name, kind, parents, trashed)";

                    Log("GetDriveFolders", "Getting drive files and folders");

                    IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute().Files;
                    if (files != null && files.Count > 0)
                    {
                        dirs = new List<CloudDriveDirInfo>();
                        foreach (var file in files)
                        {
                            if (file.Trashed == true)
                            {
                                continue;
                            }

                            var parentsId = file.Parents != null ? file.Parents[0] : "";
                            Debug.WriteLine($"Name: {file.Name} | Id: {file.Id} | parentsId: {parentsId}");
                            if (onlyRetrieveAppCloudFolder)
                            {
                                if (file.Name == _MusicDirName)
                                {
                                    _MusicDirId = file.Id;
                                    var ext = Path.GetExtension(file.Name);
                                    if (string.IsNullOrEmpty(ext))
                                    {
                                        dirs.Add(new CloudDriveDirInfo
                                        {
                                            DirName = file.Name,
                                            DirId = file.Id,
                                            DirSize = file.Size,
                                        });
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (file.Name == _MusicDirName)
                                {
                                    _MusicDirId = file.Id;
                                }
                                {
                                    var ext = Path.GetExtension(file.Name);
                                    if (string.IsNullOrEmpty(ext))
                                    {
                                        dirs.Add(new CloudDriveDirInfo
                                        {
                                            DirName = file.Name,
                                            DirId = file.Id,
                                            DirSize = file.Size,
                                        });
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Log("GetDriveFolders", "No files/dirs found.");
                    }
                }
                catch (Exception ex)
                {
                    Log("ex", ex.Message);
                }
                return dirs;
            }, _CancelToken);
        }

        public async Task<bool> CreateCloudDriveRootFolder()
        {
            try
            {
                Log("CreateCloudDriveRootFolder", "Start");

                string rootFolderId = await GetOrCreateFolder(_MusicDirName, null);

                if (string.IsNullOrEmpty(rootFolderId)) return false;

                return true;
            }
            catch (Exception ex)
            {
                Log("Error CreateCloudDriveRootFolder", ex.Message);
                return false;
            }
        }

        private async Task<string> GetOrCreateFolder(string folderName, string parentId)
        {
            string query = $"name = '{folderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            if (!string.IsNullOrEmpty(parentId))
            {
                query += $" and '{parentId}' in parents";
            }

            var listRequest = _DriveService.Files.List();
            listRequest.Q = query;
            listRequest.Fields = "files(id, name)";

            var result = await listRequest.ExecuteAsync(_CancelToken);
            var existingFolder = result.Files.FirstOrDefault();

            if (existingFolder != null)
            {
                return existingFolder.Id;
            }

            var newFolder = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder"
            };

            if (!string.IsNullOrEmpty(parentId))
            {
                newFolder.Parents = new List<string> { parentId };
            }

            var createRequest = _DriveService.Files.Create(newFolder);
            createRequest.Fields = "id";
            var folder = await createRequest.ExecuteAsync(_CancelToken);

            return folder.Id;
        }

        public async Task<List<CloudDriveFileInfo>> GetFilesforid(CloudDriveDirInfo folder, bool onlyRetrieveFirstFile)
        {
            List<CloudDriveFileInfo> driveFiles = null;
            List<Google.Apis.Drive.v3.Data.File> FilesList = new List<Google.Apis.Drive.v3.Data.File>();
            try
            {
                var resultFileList = new List<Google.Apis.Drive.v3.Data.File>();

                FilesResource.ListRequest request = _DriveService.Files.List();
                request.Q = $"'{folder.DirId}' in parents";
                request.Fields = "nextPageToken, files(id, name, trashed, webViewLink)";

                do
                {
                    try
                    {
                        FileList files = await request.ExecuteAsync();
                        resultFileList.AddRange(files.Files.Where(i => i.Trashed == false));
                        request.PageToken = files.NextPageToken;

                        var songFiles = files.Files.Where(i => i.Trashed == false).ToList();

                        List<string> songFileIds = songFiles.Select(f => f.Id).ToList();

                        FilesResource.ListRequest request2 = _DriveService.Files.List();
                        request2.Q = string.Join(" or ", songFileIds.Select(id => $"'{id}' in parents"));
                        request2.Fields = "nextPageToken, files(id, name, trashed, webViewLink)";

                        FileList files2 = await request2.ExecuteAsync();
                        var songFiles2 = files2.Files.ToList();

                        FilesList.AddRange(songFiles2);
                        request.PageToken = files2.NextPageToken;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("An error occurred: " + e.Message);
                        request.PageToken = null;
                    }
                } while (!String.IsNullOrEmpty(request.PageToken));


                if (FilesList != null)
                {
                    if (FilesList.Count > 0)
                    {
                        driveFiles = new List<CloudDriveFileInfo>();

                        foreach (var file in FilesList)
                        {
                            if (onlyRetrieveFirstFile)
                            {
                                driveFiles.Add(new CloudDriveFileInfo
                                {
                                    FileId = file.Id,
                                    FileName = file.Name,
                                    FileSize = file.Size
                                });
                                break;
                            }
                            else
                            {
                                driveFiles.Add(new CloudDriveFileInfo
                                {
                                    FileId = file.Id,
                                    FileName = file.Name,
                                    FileSize = file.Size
                                });
                            }
                        }
                    }
                    else
                    {
                        Log("GetFiles", "No files found.");
                    }
                }
                else
                {
                    Log("GetFiles", "No files found.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Log("ex GetFiles", ex.Message);
            }

            return driveFiles;
        }

        public Task<List<CloudDriveDirInfo>> GetSubFolder(CloudDriveDirInfo folder)
        {
            return Task.Run<List<CloudDriveDirInfo>>(() =>
            {
                List<CloudDriveDirInfo> dirs = null;
                try
                {
                    FilesResource.ListRequest listRequest = _DriveService.Files.List();
                    listRequest.Q = string.Format("mimeType='application/vnd.google-apps.folder' and '{0}' in parents", folder.DirId);
                    listRequest.PageSize = 100;
                    listRequest.Fields = "nextPageToken, files(id, name, kind, parents, trashed, modifiedTime)";

                    Log("GetSubFolder", "Getting sub folders");

                    IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute().Files;
                    if (files != null && files.Count > 0)
                    {
                        dirs = new List<CloudDriveDirInfo>();
                        foreach (var file in files)
                        {
                            if (file.Trashed == true)
                            {
                                continue;
                            }

                            var parentsId = file.Parents != null ? file.Parents[0] : "";

                            Debug.WriteLine($"Name: {file.Name} | Id: {file.Id} | parentsId: {parentsId}");

                            var ext = Path.GetExtension(file.Name);
                            if (string.IsNullOrEmpty(ext))
                            {
                                var dir = new CloudDriveDirInfo
                                {
                                    DirName = file.Name,
                                    DirId = file.Id,
                                    DirSize = file.Size,
                                };

                                dirs.Add(dir);
                            }
                        }
                    }
                    else
                    {
                        Log("GetSubFolder", "No dirs found.");
                    }
                }
                catch (Exception ex)
                {
                    Log("ex", ex.Message);
                }
                return dirs;
            }, _CancelToken);
        }

        public async Task<string> GetLastBackupDate(CloudDriveDirInfo folder)
        {
            try
            {
                FilesResource.ListRequest listRequest = _DriveService.Files.List();
                listRequest.Q = string.Format("mimeType='application/vnd.google-apps.folder' and '{0}' in parents", folder.DirId);
                listRequest.PageSize = 100;
                listRequest.Fields = "nextPageToken, files(id, name, kind, parents, trashed, modifiedTime)";

                Log("GetSubFolder", "Getting sub folders");

                IList<Google.Apis.Drive.v3.Data.File> files = await Task.Run(() => listRequest.Execute().Files);

                if (files != null && files.Count > 0)
                {
                    var latestFile = files.OrderBy(x => x.ModifiedTime).FirstOrDefault();
                    if (latestFile != null)
                    {
                        FilesResource.ListRequest listRequest2 = _DriveService.Files.List();
                        listRequest2.Q = $"'{latestFile.Id}' in parents";
                        listRequest2.PageSize = 100;
                        listRequest2.Fields = "nextPageToken, files(id, name, kind, parents, trashed, modifiedTime)";

                        IList<Google.Apis.Drive.v3.Data.File> files2 = await Task.Run(() => listRequest2.Execute().Files);
                        var latestFile2 = files2.OrderBy(x => x.ModifiedTime).FirstOrDefault();
                        if (files2 != null && files2.Count > 0)
                        {
                            return latestFile2.ModifiedTime.ToString();
                        }
                        else
                        {
                            Log("GetSubFolder", "No files found inside the latest folder.");
                        }
                    }
                }
                else
                {
                    Log("GetSubFolder", "No dirs found.");
                }
            }
            catch (Exception ex)
            {
                Log("ex", ex.Message);
            }

            return null;
        }

        public async Task<bool> DownloadFile(CloudFileModel fileToDownload, string destinationFilePath)
        {
            try
            {
                if (_DriveService == null)
                {
                    return false;
                }

                if (fileToDownload == null || string.IsNullOrWhiteSpace(fileToDownload.FileId))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(destinationFilePath))
                {
                    return false;
                }

                var dir = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                }

                var request = _DriveService.Files.Get(fileToDownload.FileId);
                request.SupportsAllDrives = true;
                request.AcknowledgeAbuse = true;

                request.MediaDownloader.ProgressChanged += progress =>
                {
                    try
                    {
                        switch (progress.Status)
                        {
                            case DownloadStatus.Downloading:
                                Debug.WriteLine($"Downloading: {progress.BytesDownloaded} bytes...");
                                break;

                            case DownloadStatus.Completed:
                                Debug.WriteLine("Download complete.");
                                break;

                            case DownloadStatus.Failed:
                                Debug.WriteLine("Download failed.");
                                break;
                        }
                    }
                    catch (Exception progressEx)
                    {
                        Log("DownloadProgressError", progressEx.Message);
                    }
                };

                using (var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
                {
                    var progress = await request.DownloadAsync(fileStream, _CancelToken);
                    await fileStream.FlushAsync();

                    if (progress == null || progress.Status != DownloadStatus.Completed)
                    {
                        return false;
                    }
                }

                var length = new FileInfo(destinationFilePath).Length;
                if (length <= 0)
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log("DownloadException", ex.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(destinationFilePath) && File.Exists(destinationFilePath) && new FileInfo(destinationFilePath).Length <= 0)
                    {
                        File.Delete(destinationFilePath);
                    }
                }
                catch
                {
                }
            }
        }

        private async Task DeleteAllFilesInFolder(string folderId)
        {
            try
            {
                var filesRequest = _DriveService.Files.List();
                filesRequest.Q = $"'{folderId}' in parents and trashed = false";
                filesRequest.Fields = "files(id, name)";
                var files = await filesRequest.ExecuteAsync(_CancelToken);

                if (files.Files != null && files.Files.Count > 0)
                {
                    foreach (var file in files.Files)
                    {
                        await _DriveService.Files.Delete(file.Id).ExecuteAsync(_CancelToken);
                        Log("DeleteAllFilesInFolder", $"Deleted file: {file.Name}");
                    }
                }
                else
                {
                    Log("DeleteAllFilesInFolder", "No files found in the folder.");
                }
            }
            catch (Exception ex)
            {
                Log("DeleteAllFilesInFolder Exception", ex.Message);
            }
        }

        public async Task<bool> DeleteFilerequest(string filepathTodelete)
        {
            var res = false;
            try
            {
                if (string.IsNullOrEmpty(_MusicDirId))
                {
                    var rootFolders = await GetDriveFolders(true);
                    if (rootFolders != null && rootFolders.Count > 0)
                    {
                        _MusicDirId = rootFolders[0].DirId;
                    }
                }

                if (!string.IsNullOrEmpty(_MusicDirId))
                {
                    var path = filepathTodelete;
                    bool isDatabaseFile = Path.GetFileName(path).Equals("appdata.dat", StringComparison.OrdinalIgnoreCase);

                    if (isDatabaseFile)
                    {
                        var subfolders = await GetSubFolder(new CloudDriveDirInfo { DirId = _MusicDirId });
                        if (subfolders != null)
                        {
                            var backupFolder = subfolders.FirstOrDefault(s => s.DirName.Equals("Backup", StringComparison.OrdinalIgnoreCase));
                            if (backupFolder != null)
                            {
                                await DeleteAllFilesInFolder(backupFolder.DirId);
                                res = true;
                            }
                        }
                    }
                    else
                    {
                        if (!System.IO.File.Exists(path))
                        {
                            var subfolders = await GetSubFolder(new CloudDriveDirInfo { DirId = _MusicDirId });
                            if (subfolders != null)
                            {
                                foreach (var sub in subfolders)
                                {
                                    if (sub.DirName.Equals("Backup", StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    var fileInSub = await GetExistingFileWithName(Path.GetFileName(path), sub.DirId);
                                    if (fileInSub != null)
                                    {
                                        var deleted = await DeleteFile(fileInSub.Id);
                                        if (deleted)
                                        {
                                            res = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ex DeleteFilerequest", ex.Message);
            }
            return res;
        }

        private async Task<Google.Apis.Drive.v3.Data.File> GetExistingFileWithName(string fileName, string folderId)
        {
            try
            {
                var request = _DriveService.Files.List();
                request.Q = $"'{folderId}' in parents and name='{fileName}' and trashed=false";
                var result = await request.ExecuteAsync();

                return result.Files.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log("ex GetExistingFileWithName", ex.Message);
                return null;
            }
        }

        public Task<bool> Logout()
        {
            return Task.Run<bool>(() =>
            {
                var res = false;
                try
                {
                    Log("Logout", "Initialize");
                    string credPath = _config.GoogleDriveTokenFile;
                    if (System.IO.File.Exists(credPath))
                    {
                        System.IO.File.Delete(credPath);
                    }

                    if (System.IO.Directory.Exists(credPath))
                    {
                        System.IO.Directory.Delete(credPath, true);
                    }

                    Log("Logout", "Success");
                    res = true;
                }
                catch (Exception ex)
                {
                    Log("ex Logout", ex.Message);
                }
                return res;
            }, _CancelToken);
        }

        public void Dispose()
        {
            try
            {
                if (_DriveService != null)
                {
                    _DriveService.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log("Dispose ex", ex.Message);
            }
        }

        private static int _uploadFileCallCount = 0;

        public async Task<bool> UploadFile(string filepathToUpload, string parentFolderIdToUpload)
        {
            _uploadFileCallCount++;

            var res = false;
            try
            {
                var path = filepathToUpload;
                if (System.IO.File.Exists(path))
                {
                    var FileMetaData = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = Path.GetFileName(path),
                        MimeType = GetMimeMapping(path),
                        Parents = new List<string> { parentFolderIdToUpload }
                    };

                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        Log("UploadFile", $"UploadFile called {_uploadFileCallCount} time(s). File: {filepathToUpload}");
                        var request = _DriveService.Files.Create(FileMetaData, stream, FileMetaData.MimeType);
                        request.Fields = "id";
                        Log("UploadFile", "Starting upload...");

                        var response = await request.UploadAsync();
                        if (response.Status == Google.Apis.Upload.UploadStatus.Completed)
                        {
                            res = true;
                            Log("UploadFile", "Upload completed successfully.");
                        }
                        else
                        {
                            Log("UploadFile", $"Upload failed. Status: {response.Status}");
                        }
                    }
                }
                else
                {
                    Log("UploadFile", $"File not found: {filepathToUpload}");
                }
            }
            catch (Exception ex)
            {
                Log("ex UploadFile", ex.Message);
            }
            return res;
        }

        public async Task<string> CreateFolder(string folderName, string ParentFolderId)
        {
            string resFolderId = null;
            try
            {
                Log("CreateFolder", "Start");
                if (_DriveService == null)
                    return null;

                var listRequest = _DriveService.Files.List();
                string parentQuery = string.IsNullOrEmpty(ParentFolderId) ? "'root'" : $"'{ParentFolderId}'";
                listRequest.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{folderName}' and {parentQuery} in parents and trashed = false";
                listRequest.Fields = "files(id, name)";
                var existingFiles = await listRequest.ExecuteAsync(_CancelToken);
                
                if (existingFiles.Files != null && existingFiles.Files.Count > 0)
                {
                    resFolderId = existingFiles.Files[0].Id;
                    Log("CreateFolder existing id", resFolderId);
                    
                    if (string.IsNullOrEmpty(ParentFolderId))
                    {
                        _MusicDirId = resFolderId;
                    }
                    return resFolderId;
                }

                Google.Apis.Drive.v3.Data.File folderToCreate = null;

                if (ParentFolderId == null)
                {
                    folderToCreate = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = folderName,
                        Description = "document description",
                        MimeType = "application/vnd.google-apps.folder",
                    };
                }
                else
                {
                    folderToCreate = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = folderName,
                        Description = "document description",
                        MimeType = "application/vnd.google-apps.folder",
                        Parents = new[] { ParentFolderId }
                    };
                }

                var file = _DriveService.Files.Create(folderToCreate);
                var dirDetails = await file.ExecuteAsync(_CancelToken);
                if (dirDetails != null)
                {
                    if (string.IsNullOrEmpty(ParentFolderId))
                    {
                        _MusicDirId = dirDetails.Id;
                    }

                    resFolderId = dirDetails.Id;
                    Log("CreateFolder id", resFolderId);
                }
            }
            catch (Exception ex)
            {
                Log("ex CreateFolder", ex.Message);
            }
            return resFolderId;
        }

        public async Task<string> CreatesubFolder(string folderName, string ParentFolderId)
        {
            return await CreateFolder(folderName, ParentFolderId);
        }

        public async Task<bool> DeleteFile(string fileId)
        {
            var res = false;
            try
            {
                Log("DeleteFile", "Start");
                FilesResource.DeleteRequest request = _DriveService.Files.Delete(fileId);
                var response = await request.ExecuteAsync(_CancelToken);
                res = true;
                Log("DeleteFile", res.ToString());
            }
            catch (Exception ex)
            {
                Log("ex DeleteFile", ex.Message);
            }
            return res;
        }

        public Task<bool> ShareFile(string toEmail, string cloudFileId)
        {
            return Task.Run(() =>
            {
                var res = false;
                try
                {
                    Log("ShareFile", "Start");
                    GDrivePermission newPermission = new GDrivePermission();
                    newPermission.Type = "user";
                    newPermission.EmailAddress = toEmail;
                    newPermission.Role = "reader";
                    var drivePermissions = _DriveService.Permissions;
                    var resPermission = _DriveService.Permissions.Create(newPermission, cloudFileId).Execute();
                    if (resPermission != null)
                    {
                        res = true;
                    }
                    Log("ShareFile", res.ToString());
                }
                catch (Exception ex)
                {
                    Log("ex ShareFile", ex.Message);
                }
                return res;
            }, _CancelToken);
        }

        public Task<bool> ChangeFilepermission(string toEmail, string cloudFileId)
        {
            return Task.Run(() =>
            {
                var res = false;
                try
                {
                    Log("ShareFile", "Start");

                    var newPermission = new GDrivePermission
                    {
                        Type = "anyone",
                        Role = "reader"
                    };

                    var drivePermissions = _DriveService.Permissions;
                    var request = _DriveService.Permissions.Create(newPermission, cloudFileId);
                    request.Fields = "id";

                    var resPermission = request.Execute();
                    if (resPermission != null)
                    {
                        res = true;
                    }

                    Log("ShareFile", res.ToString());
                }
                catch (Exception ex)
                {
                    Log("ex ShareFile", ex.Message);
                }
                return res;
            }, _CancelToken);
        }

        public async Task<bool> UnShareFile(string cloudFileId, string emailAddress)
        {
            var res = false;
            try
            {
                Log("UnShareFile", "Start");

                PermissionsResource.ListRequest request = _DriveService.Permissions.List(cloudFileId);
                request.Fields = "permissions(id, emailAddress)";

                var resPermission = await request.ExecuteAsync();
                if (resPermission != null)
                {
                    if (resPermission.Permissions != null)
                    {
                        var permission = resPermission.Permissions.FirstOrDefault(i => i.EmailAddress == emailAddress);
                        if (permission != null)
                        {
                            var deleteRequest = _DriveService.Permissions.Delete(cloudFileId, permission.Id);
                            var requestResponse = await deleteRequest.ExecuteAsync();
                            Log("UnShareFile", "successfully");
                            res = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ex UnShareFile", ex.Message);
            }
            return res;
        }

        public async Task<bool> DeleteFolderById(string folderId)
        {
            bool isDeleted = false;
            try
            {
                Log("DeleteFolderById", $"Attempting to delete folder with ID: {folderId}");

                var deleteRequest = _DriveService.Files.Delete(folderId);
                await deleteRequest.ExecuteAsync(_CancelToken);

                isDeleted = true;
                Log("DeleteFolderById", $"Successfully deleted folder with ID: {folderId}");
            }
            catch (Google.GoogleApiException ex)
            {
                Log("DeleteFolderById GoogleApiException", ex.Message);
            }
            catch (Exception ex)
            {
                Log("DeleteFolderById Exception", ex.Message);
            }

            return isDeleted;
        }

        public bool CancelTask()
        {
            var res = false;
            try
            {
                _CancelTokenSource.Cancel();
                res = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return res;
        }

        public Task<bool> RefreshToken()
        {
            throw new NotImplementedException();
        }

        public async Task<bool> RenameCloudFile(string newName, string cloudFileId)
        {
            try
            {
                var service = _DriveService;

                if (service == null)
                {
                    Debug.WriteLine("Drive service not initialized.");
                    return false;
                }

                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = newName
                };

                var request = service.Files.Update(fileMetadata, cloudFileId);
                request.Fields = "id, name";

                var updatedFile = await request.ExecuteAsync();

                Debug.WriteLine($"File renamed successfully to: {updatedFile.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RenameCloudFile error: {ex.Message}");
                return false;
            }
        }
    }
}
