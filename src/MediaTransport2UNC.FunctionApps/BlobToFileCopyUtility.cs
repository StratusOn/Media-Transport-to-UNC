using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.File;

// This class has no namespace to make it easier to include in both the local Function App project (compiled DLL, debug with VS) as well as a helper class to the CSX Function App C# script file.
public static class BlobToFileCopyUtility
{
    public static async Task CopyBlockBlobToFile(CloudBlockBlob sourceBlob, TraceWriter log)
    {
        try
        {
            // 1) Get source file settings.
            string targetConnectionString = ConfigurationManager.AppSettings["TARGET_STORAGEACCOUNT_CONNECTIONSTRING"];
            string fileShareName = ConfigurationManager.AppSettings["TARGET_FILESHARE_NAME"];
            string fileShareFolderName = ConfigurationManager.AppSettings["TARGET_FILESHARE_FOLDERNAME"];
            string targetFileName = sourceBlob.Name;
            bool deleteSourceBlobAfterTransferSuccess = bool.Parse(ConfigurationManager.AppSettings["DELETE_SOURCEBLOB_AFTER_TRANSFER_SUCCESS"]);

            log.Info($"[Input]: Target File Name: {targetFileName}");
            log.Info($"[Setting]: Target Connection String: {targetConnectionString}");
            log.Info($"[Setting]: File Share Name: {fileShareName}");
            log.Info($"[Setting]: File Share Folder Name: {fileShareFolderName}");
            log.Info($"[Setting]: Delete Source Blob After Transfer Success: {deleteSourceBlobAfterTransferSuccess}");

            // 2) Copy to Azure Files.
            await CopyBlobToAzureFiles(sourceBlob, targetConnectionString, fileShareName, fileShareFolderName, targetFileName, log);

            if (deleteSourceBlobAfterTransferSuccess)
            {
                log.Info($"Deleting source blob ({sourceBlob.Name}) after successful transfer since file share since DELETE_SOURCEBLOB_AFTER_TRANSFER_SUCCESS=true.");
                await sourceBlob.DeleteAsync();
            }
        }
        catch (Exception exception)
        {
            log.Error($"Error in CopyBlockBlobToFile: {exception.Message}\n{exception.StackTrace}");
        }
    }

    private static async Task CopyBlobToAzureFiles(CloudBlockBlob sourceBlob, string targetConnectionString, string fileShareName, string fileShareFolderName, string targetFileName, TraceWriter log)
    {
        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(targetConnectionString);
        CloudFileClient fileClient = storageAccount.CreateCloudFileClient();
        CloudFileShare share = fileClient.GetShareReference(fileShareName);

        if (!share.Exists())
        {
            log.Info($"Creating file share since it does not exist: {fileShareName}");
            await share.CreateAsync();
        }

        CloudFileDirectory rootDir = share.GetRootDirectoryReference();
        CloudFileDirectory targetDir = rootDir.GetDirectoryReference(fileShareFolderName);
        await targetDir.CreateIfNotExistsAsync();
        string localFilePath = GetLocalFilePath(sourceBlob.Name, log);
        Stopwatch sw = new Stopwatch();

        try
        {
            log.Info($"Downloading to local file: {localFilePath}");
            sw.Start();
            await sourceBlob.DownloadToFileAsync(localFilePath, FileMode.Create);
            sw.Stop();
            log.Info($"Successfully downloaded (in {sw.ElapsedMilliseconds} msecs) to local file: {localFilePath}");

            string[] sourceFolders = sourceBlob.Name.Split('/');
            if (sourceFolders.Length > 1)
            {
                for (int i = 0; i < sourceFolders.Length - 1; i++)
                {
                    var subfolderName = sourceFolders[i];
                    log.Info($"Creating subfolder: {subfolderName}");
                    targetDir = targetDir.GetDirectoryReference(subfolderName);
                    await targetDir.CreateIfNotExistsAsync();
                }

                CloudFile file = targetDir.GetFileReference(sourceFolders[sourceFolders.Length - 1]);
                log.Info($"Uploading local file to target share under subfolder: {file.Uri.AbsoluteUri}");
                sw.Restart();
                await file.UploadFromFileAsync(localFilePath);
                sw.Stop();
                log.Info($"Successfully uploaded (in {sw.ElapsedMilliseconds} msecs) root local file to target share under subfolder: {file.Uri.AbsoluteUri}");
            }
            else
            {
                CloudFile file = targetDir.GetFileReference(targetFileName);
                log.Info($"Uploading local file to target share root folder: {file.Uri.AbsoluteUri}");
                sw.Restart();
                await file.UploadFromFileAsync(localFilePath);
                sw.Stop();
                log.Info($"Successfully uploaded (in {sw.ElapsedMilliseconds} msecs) local file to target share root folder: {file.Uri.AbsoluteUri}");
            }
        }
        finally
        {
            if (File.Exists(localFilePath))
            {
                log.Info($"Deleting local file: {localFilePath}");
                File.Delete(localFilePath);
            }
        }
    }

    private static string GetLocalFilePath(string fileName, TraceWriter log)
    {
        string folderName = $"{new Random().Next(10000000, 99999999)}";
        string tempFolderPath = Path.Combine(Path.GetTempPath(), folderName);
        string localFilePath = Path.Combine(tempFolderPath, fileName);
        var localFile = new FileInfo(localFilePath);
        if (!localFile.Directory.Exists)
        {
            log.Info($"Creating local temp directory: {localFile.Directory}");
            localFile.Directory.Create();
        }

        log.Info($"Local File Path: {localFile.FullName}");
        return localFile.FullName;
    }
}
