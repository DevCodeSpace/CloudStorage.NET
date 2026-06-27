using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudStorage.Net
{
    public interface ICloudDrive : IDisposable
    {
        CloudDriveOptions CloudDriveType { get; }
        string CloudDriveBackupDirName { get; }
        string CloudDriveMusicDownloadDir { get; }
        string CloudUserEmail { get; set; }
        CancellationTokenSource _CancelTokenSource { get; }
        CancellationToken _CancelToken { get; }

        Task<bool> AuthenticateDrive();
        Task<bool> AuthenticateOnly();
        Task<bool> RefreshToken();
        Task<CloudDriveInfo> GetDriveInfo();
        Task<string> GetSongShareLink(string cloudFileId);
        Task<List<CloudDriveFileInfo>> GetFiles(CloudDriveDirInfo folder, bool onlyRetrieveFirstFile);
        Task<bool> CreateCloudDriveRootFolder();
        Task<List<CloudDriveFileInfo>> GetFilesforid(CloudDriveDirInfo folder, bool onlyRetrieveFirstFile);
        Task<List<CloudDriveDirInfo>> GetSubFolder(CloudDriveDirInfo folder);
        Task<string> GetLastBackupDate(CloudDriveDirInfo folder);
        Task<List<CloudDriveDirInfo>> GetDriveFolders(bool onlyRetrieveAppCloudFolder);
        Task<bool> Logout();
        Task<bool> DownloadFile(CloudFileModel fileToDownload, string destinationFilePath);
        Task<bool> DeleteFilerequest(string filepathTodelete);
        Task<bool> UploadFile(string filepathToUpload, string parentFolderIdToUpload);
        Task<string> CreateFolder(string folderName, string ParentFolderId);
        Task<string> CreatesubFolder(string folderName, string ParentFolderId);
        Task<bool> DeleteFile(string fileId);
        Task<bool> DeleteFolderById(string folderId);
        Task<bool> ShareFile(string toUser, string cloudFileId);
        Task<bool> ChangeFilepermission(string toUser, string cloudFileId);
        Task<bool> UnShareFile(string cloudFileId, string emailAddress);
        Task<bool> RenameCloudFile(string newName, string cloudFileId);
        bool CancelTask(); 
    }
}
