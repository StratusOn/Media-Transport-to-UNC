using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.File;

// This class has no namespace to make it easier to include in both the local Function App project (compiled DLL, debug with VS) as well as a helper class to the CSX Function App C# script file.
public static class BlobToFileCopyUtility
{
    private const double SAS_EXPIRATION_IN_HOURS = 24;

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
            throw;
        }
    }

    private static async Task CopyBlobToAzureFiles(CloudBlockBlob sourceBlob, string targetConnectionString, string fileShareName, string fileShareFolderName, string targetFileName, TraceWriter log)
    {
        // Reference: https://github.com/Azure-Samples/storage-file-dotnet-getting-started/blob/master/FileStorage/GettingStarted.cs
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

            targetFileName = sourceFolders[sourceFolders.Length - 1];
        }

        // Copy the source file to a temporary file.
        string targetTempFileName = $"{targetFileName}.tmp";
        CloudFile tempFile = targetDir.GetFileReference(targetTempFileName);
        string blobSas = sourceBlob.GetSharedAccessSignature(new SharedAccessBlobPolicy()
        {
            Permissions = SharedAccessBlobPermissions.Read,
            SharedAccessExpiryTime = DateTime.UtcNow.AddHours(SAS_EXPIRATION_IN_HOURS)
        });

        var blobSasUri = new Uri($"{sourceBlob.StorageUri.PrimaryUri}{blobSas}");
        log.Info($"Source blob SAS URL (expires in {SAS_EXPIRATION_IN_HOURS} hours): {blobSasUri}");
        log.Info($"Copying source blob to temporary target file share: {tempFile.Uri.AbsoluteUri}");
        Stopwatch sw = new Stopwatch();
        sw.Start();
        await tempFile.StartCopyAsync(blobSasUri);
        sw.Stop();
        log.Info($"Successfully copied (in {sw.ElapsedMilliseconds} msecs) source blob to temporary target file share: {tempFile.Uri.AbsoluteUri}");

        // Rename the copied temporary file to original name.
        CloudFile file = targetDir.GetFileReference(targetFileName);
        log.Info($"Copying temporary target file to final target file: {file.Uri.AbsoluteUri}");
        sw.Restart();
        await file.StartCopyAsync(tempFile);
        sw.Stop();
        log.Info($"Successfully copied (in {sw.ElapsedMilliseconds} msecs) temporary target file to final target file: {file.Uri.AbsoluteUri}");

        // Delete the temporary file.
        log.Info($"Deleting temporary target file: {tempFile.Uri.AbsoluteUri}");
        sw.Restart();
        await tempFile.DeleteAsync();
        sw.Stop();
        log.Info($"Successfully deleted (in {sw.ElapsedMilliseconds} msecs) temporary target file: {tempFile.Uri.AbsoluteUri}");
    }
}
