using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using CloudStorage.Abstractions;
using CloudStorage.GoogleDrive;
using CloudStorage.Dropbox;
using CloudStorage.Net;
using Newtonsoft.Json;

namespace CloudStorage.Net.TestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("    Generic Cloud Storage Test Console");
            Console.WriteLine("==================================================");

            Console.WriteLine("\nSelect Storage Provider:");
            Console.WriteLine("1. Google Drive (CloudStorage.GoogleDrive)");
            Console.WriteLine("2. Dropbox (CloudStorage.Dropbox)");
            Console.Write("Enter choice (1-2): ");
            string providerChoice = Console.ReadLine()?.Trim();

            ICloudStorageService storageService;

            if (providerChoice == "2")
            {
                string secretFile = "dropbox_app_secrets.json";
                string appKey = null;
                string appSecret = null;

                if (File.Exists(secretFile))
                {
                    try
                    {
                        var secrets = JsonConvert.DeserializeAnonymousType(
                            File.ReadAllText(secretFile),
                            new { AppKey = "", AppSecret = "" }
                        );
                        appKey = secrets?.AppKey;
                        appSecret = secrets?.AppSecret;
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(appSecret))
                {
                    Console.WriteLine("\n[Setup] Dropbox App credentials not found.");
                    Console.Write("Enter your Dropbox App Key: ");
                    appKey = Console.ReadLine()?.Trim();
                    Console.Write("Enter your Dropbox App Secret: ");
                    appSecret = Console.ReadLine()?.Trim();

                    if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(appSecret))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error: Both App Key and App Secret are required.");
                        Console.ResetColor();
                        return;
                    }

                    try
                    {
                        File.WriteAllText(secretFile, JsonConvert.SerializeObject(new { AppKey = appKey, AppSecret = appSecret }, Formatting.Indented));
                        Console.WriteLine($"Saved app credentials to: {Path.GetFullPath(secretFile)}");
                    }
                    catch { }
                }

                Console.WriteLine("\nInitializing Dropbox Storage Service...");
                storageService = new DropboxStorageService(appKey, appSecret);
            }
            else
            {
                // Default to Google Drive
                string defaultSecretPath = "google_drive_client_secret.json";
                string tokenPath = "token.json";

                Console.WriteLine($"\nLooking for client secrets file at: {Path.GetFullPath(defaultSecretPath)}");
                if (!File.Exists(defaultSecretPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[Warning] Client secrets file not found!");
                    Console.ResetColor();
                    Console.WriteLine("Please download your client secrets JSON from the Google Cloud Console.");
                    Console.Write("Enter custom path to client secrets JSON (or press Enter to exit/retry): ");
                    string customPath = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(customPath))
                    {
                        defaultSecretPath = customPath;
                    }

                    if (!File.Exists(defaultSecretPath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error: Client secrets file at '{defaultSecretPath}' does not exist.");
                        Console.ResetColor();
                        Console.WriteLine("Exiting. Place your client secrets file in the directory or specify a valid path.");
                        return;
                    }
                }

                var config = new GoogleDriveConfig
                {
                    RootFolderName = "GoogleDriveNetTestFolder",
                    GoogleDriveAppName = "GoogleDriveNetTestApp",
                    GoogleDriveTokenFile = tokenPath,
                    ClientSecretPath = defaultSecretPath,
                    GoogleDriveMusic = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads")
                };

                Console.WriteLine("\nInitializing Google Drive Storage Service...");
                storageService = new GoogleDriveStorageService(config);
            }

            // Hook up Logging
            storageService.LogCallback = (tag, message) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[{tag}] ");
                Console.ResetColor();
                Console.WriteLine(message);
            };

            bool exit = false;
            bool authenticated = false;

            while (!exit)
            {
                Console.WriteLine("\n--------------------------------------------------");
                Console.WriteLine($"      MENU - Provider: {storageService.ProviderName}      ");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine($"Status: {(authenticated ? "Authenticated" : "Not Authenticated")}");
                Console.WriteLine("1. Authenticate Service");
                Console.WriteLine("2. Retrieve Storage/Account Info");
                Console.WriteLine("3. Create a Directory/Folder");
                Console.WriteLine("4. List Sub-directories");
                Console.WriteLine("5. List Files in a Directory");
                Console.WriteLine("6. Upload a File");
                Console.WriteLine("7. Download a File");
                Console.WriteLine("8. Delete a File (by ID/Path)");
                Console.WriteLine("9. Logout / Reset Connection");
                Console.WriteLine("0. Exit");
                Console.Write("Choose an option: ");

                string choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            Console.WriteLine("Authenticating...");
                            authenticated = await storageService.AuthenticateAsync();
                            if (authenticated)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("Authentication successful!");
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Authentication failed.");
                            }
                            Console.ResetColor();
                            break;

                        case "2":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            var info = await storageService.GetStorageSpaceInfoAsync();
                            if (info != null)
                            {
                                Console.WriteLine($"Storage Name: {info.DriveName}");
                                Console.WriteLine($"User/Owner: {info.OwnerEmail}");
                                Console.WriteLine($"Used Space: {FormatBytes(info.UsedSpace)}");
                                Console.WriteLine($"Total Limit: {FormatBytes(info.TotalSpace)}");
                            }
                            else
                            {
                                Console.WriteLine("Failed to retrieve storage space info.");
                            }
                            break;

                        case "3":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            Console.Write("Enter new directory name: ");
                            string dirName = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(dirName)) break;

                            Console.Write("Enter parent directory ID/Path (leave empty for root): ");
                            string parentDirId = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(parentDirId)) parentDirId = null;

                            Console.WriteLine($"Creating/verifying directory '{dirName}'...");
                            var createdDir = await storageService.GetOrCreateDirectoryAsync(dirName, parentDirId);
                            if (createdDir != null)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"Directory created/verified successfully! ID/Path: {createdDir.Id}");
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.WriteLine("Failed to create directory.");
                            }
                            break;

                        case "4":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            Console.Write("Enter parent directory ID/Path (leave empty for root): ");
                            string listParentId = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(listParentId)) listParentId = null;

                            Console.WriteLine("Fetching sub-directories...");
                            var subDirs = await storageService.GetSubDirectoriesAsync(listParentId);
                            if (subDirs != null && subDirs.Count > 0)
                            {
                                Console.WriteLine("\nSub-directories found:");
                                foreach (var sub in subDirs)
                                {
                                    Console.WriteLine($"- {sub.Name} (ID/Path: {sub.Id})");
                                }
                            }
                            else
                            {
                                Console.WriteLine("No sub-directories found.");
                            }
                            break;

                        case "5":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            Console.Write("Enter Directory ID/Path (or 'root' for root): ");
                            string folderId = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(folderId)) folderId = "root";

                            Console.WriteLine($"Listing files for directory: {folderId}...");
                            var files = await storageService.GetFilesAsync(folderId);
                            if (files != null && files.Count > 0)
                            {
                                Console.WriteLine("\nFiles found:");
                                foreach (var file in files)
                                {
                                    Console.WriteLine($"- {file.Name} (ID/Path: {file.Id}, Size: {FormatBytes(file.Size)})");
                                }
                            }
                            else
                            {
                                Console.WriteLine("No files found in the specified directory.");
                            }
                            break;

                        case "6":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            Console.Write("Enter local file path to upload: ");
                            string uploadPath = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(uploadPath) || !File.Exists(uploadPath))
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Error: File does not exist locally.");
                                Console.ResetColor();
                                break;
                            }

                            Console.WriteLine("Tip: Use 'root' to upload to the top-level directory,");
                            Console.WriteLine("     or enter a directory ID/Path from options 3 or 4.");
                            Console.Write("Enter destination Directory ID/Path (default is 'root'): ");
                            string destFolderId = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(destFolderId))
                            {
                                destFolderId = "root";
                            }

                            Console.WriteLine($"Uploading to directory '{destFolderId}'...");
                            bool uploaded = await storageService.UploadFileAsync(uploadPath, destFolderId);
                            Console.WriteLine(uploaded ? "Upload completed!" : "Upload failed.");
                            break;

                        case "7":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            Console.Write("Enter File ID/Path to download: ");
                            string downloadFileId = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(downloadFileId)) break;

                            Console.Write("Enter destination local file path (e.g. D:\\download.txt): ");
                            string downloadDest = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(downloadDest)) break;

                            Console.WriteLine("Downloading...");
                            bool downloaded = await storageService.DownloadFileAsync(downloadFileId, downloadDest);
                            Console.WriteLine(downloaded ? $"Downloaded successfully to {downloadDest}!" : "Download failed.");
                            break;

                        case "8":
                            if (!authenticated) { Console.WriteLine("Please authenticate first."); break; }
                            Console.Write("Enter File ID/Path to delete: ");
                            string deleteFileId = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(deleteFileId)) break;

                            Console.WriteLine("Deleting...");
                            bool deleted = await storageService.DeleteFileAsync(deleteFileId);
                            Console.WriteLine(deleted ? "File deleted." : "Failed to delete file.");
                            break;

                        case "9":
                            Console.WriteLine("Logging out...");
                            bool loggedOut = await storageService.LogoutAsync();
                            if (loggedOut)
                            {
                                authenticated = false;
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("Logout successful.");
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Logout failed.");
                            }
                            Console.ResetColor();
                            break;

                        case "0":
                            exit = true;
                            Console.WriteLine("Goodbye!");
                            break;

                        default:
                            Console.WriteLine("Invalid option. Please try again.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"An error occurred during operation: {ex.Message}");
                    Console.ResetColor();
                }
            }

            storageService.Dispose();
        }

        private static string FormatBytes(long? bytes)
        {
            if (!bytes.HasValue) return "Unknown";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double doubleBytes = bytes.Value;
            int i = 0;
            while (doubleBytes >= 1024 && i < suffixes.Length - 1)
            {
                doubleBytes /= 1024;
                i++;
            }
            return $"{doubleBytes:F2} {suffixes[i]}";
        }
    }
}
