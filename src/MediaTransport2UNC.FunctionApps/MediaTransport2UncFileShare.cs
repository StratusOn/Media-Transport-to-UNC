//#r "Microsoft.WindowsAzure.Storage"

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;

namespace MediaTransport2UNC.FunctionApps
{
    public static class MediaTransport2UncFileShare
    {
        [FunctionName("MediaTransport2UncFileShare")]
        public static async Task Run([BlobTrigger("uploads/{name}", Connection = "UploadsStorage")]CloudBlockBlob blob, string name, TraceWriter log)
        {
            log.Info($"C# Blob trigger function Processed blob\nSource Name: {name} \nSource Uri: {blob.Uri}");
            await BlobToFileCopyUtility.CopyBlockBlobToFile(blob, log);
        }
    }
}
