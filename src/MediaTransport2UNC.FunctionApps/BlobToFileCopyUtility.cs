using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.File;

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
            // Assumes the name does not contain separators (e.g. "/" to designate nested folders):
            string targetFileName = sourceBlob.Name;
            bool deleteSourceBlobAfterTransferSuccess = bool.Parse(ConfigurationManager.AppSettings["DELETE_SOURCEBLOB_AFTER_TRANSFER_SUCCESS"]);

            // 2) Copy to Azure Files.
            await CopyBlobToAzureFiles(sourceBlob, targetConnectionString, fileShareName, fileShareFolderName, targetFileName, log);

            if (deleteSourceBlobAfterTransferSuccess)
            {
                await sourceBlob.DeleteAsync();
            }
        }
        catch (Exception exception)
        {
            log.Error($"Error: {exception.Message}\n{exception.StackTrace}");
        }
    }

    private static async Task CopyBlobToAzureFiles(CloudBlockBlob sourceBlob, string targetConnectionString, string fileShareName, string fileShareFolderName, string targetFileName, TraceWriter log)
    {
        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(targetConnectionString);
        CloudFileClient fileClient = storageAccount.CreateCloudFileClient();
        CloudFileShare share = fileClient.GetShareReference(fileShareName);

        if (!share.Exists())
        {
            await share.CreateAsync();
        }

        CloudFileDirectory rootDir = share.GetRootDirectoryReference();
        CloudFileDirectory targetDir = rootDir.GetDirectoryReference(fileShareFolderName);
        await targetDir.CreateIfNotExistsAsync();
        CloudFile file = targetDir.GetFileReference(targetFileName);
        string localFilePath = GetLocalFilePath(sourceBlob.Name, log);

        try
        {
            await sourceBlob.DownloadToFileAsync(localFilePath, FileMode.Create);
            log.Info($"Successfully downloaded to local file: {localFilePath}");
            await file.UploadFromFileAsync(localFilePath);
            log.Info($"Successfully uploaded local file to target share: {file.Uri.AbsoluteUri}");
        }
        finally
        {
            if (File.Exists(localFilePath))
            {
                File.Delete(localFilePath);
                log.Info($"Successfully deleted local file: {localFilePath}");
            }
        }
    }

    private static string GetLocalFilePath(string fileName, TraceWriter log)
    {
        string folderName = $"{new Random().Next(10000000, 99999999)}";
        string tempFolderPath = Path.Combine(Path.GetTempPath(), folderName);
        if (!Directory.Exists(tempFolderPath))
        {
            Directory.CreateDirectory(tempFolderPath);
        }

        string localFilePath = Path.Combine(tempFolderPath, fileName);
        log.Info($"Local File Path: {localFilePath}");
        return localFilePath;
    }
}
