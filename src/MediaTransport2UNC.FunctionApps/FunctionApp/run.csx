#r "Microsoft.WindowsAzure.Storage"
#load "BlobToFileCopyUtility.cs"

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;

public static async Task Run(CloudBlockBlob blob, string name, TraceWriter log)
{
    log.Info($"C# Blob trigger function Processed blob\nSource Name: {name} \nSource Uri: {blob.Uri}");
    await BlobToFileCopyUtility.CopyBlockBlobToFile(blob, log);
}
