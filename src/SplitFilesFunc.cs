using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Linq;
using Microsoft.Azure.ServiceBus;
using System.Text;
using Microsoft.Azure.ServiceBus.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace MyCompany.Function
{
    public class SplitFilesFunc
    {
        /// <summary>
        /// Split the large file into smaller files based off the task number
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageActions"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [return: ServiceBus("parsefilequeue", Connection = "ServiceBusConnectionAppSetting")]
        [FunctionName("SplitFilesFunc")]
        public async  Task<string> Run(
            [ServiceBusTrigger("linequeue", Connection = "ServiceBusConnectionAppSetting")] ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions,
            ILogger log
            )
        {
            string myQueueItem = Encoding.UTF8.GetString(message.Body);
            log.LogInformation($"C# ServiceBus queue trigger function processed message SplitFilesFunc: {myQueueItem}");
            List<string> files = new List<string>();
            dynamic json = JsonConvert.DeserializeObject(myQueueItem);
            string url = json.url;
            string count = json.count;//amount of lines that should be in each file
            log.LogInformation($"C# ServiceBus queue trigger function processed message SplitFilesFunc: count: {count} url: {url}");

            string userAssignedClientId = Environment.GetEnvironmentVariable("User_Assigned_Managed_Identity_ClientID");
            //Default is Azure commerical, need to expliclity add Azure Government using a User Assigned Managed Idenitity
            var options = new DefaultAzureCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzureGovernment, ManagedIdentityClientId = userAssignedClientId };
            BlobClient client = new BlobClient(new Uri(url), new DefaultAzureCredential(options));

            //Get the file name
            string file = client.Name;

            //Write file to destination
            //TODO: Error handling and Check if setting exists
            string FileStorageContainer = Environment.GetEnvironmentVariable("File_Storage_Container");
            string FileStorageUrl = Environment.GetEnvironmentVariable("File_Storage_Url");
            int fileCount = Convert.ToInt32(Environment.GetEnvironmentVariable("Task_Number"));

            using (StreamReader streamReader = new StreamReader(client.OpenRead(null)))//fileStream))
            {
                
                //TODO: Check if NAN
                int linesPerFile = Convert.ToInt32(count);
                for (int i = 0; i < fileCount; i++)
                {
                    string fileName = $"{file}_{i}.txt";
                    files.Add(fileName);
  
                    var tempPath = Path.Combine(Path.GetTempPath(), fileName);
                    log.LogInformation($" File Name: {fileName} count: {count}");
                    bool IsWrite = false;
                    using (FileStream newFileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(newFileStream))
                        {
                            

                            for (int linesInCurrentFile = 0;
                                linesInCurrentFile < linesPerFile ||
                                (i == fileCount - 1 && !streamReader.EndOfStream); //Write any remaining lines (due to rounding) to the last file.
                                linesInCurrentFile++)
                            {
                                string line = await streamReader.ReadLineAsync();
                                IsWrite = true;
                                await streamWriter.WriteLineAsync(line);
                                
                            }
                        }
                    }
                    if (IsWrite)
                    {
                        using (FileStream openFileStream = new FileStream(tempPath, FileMode.Open))
                        {
                            //Write file to local disk for uploading to storage account
                            log.LogInformation($"Writing file: {tempPath}");
                            var blobClient = new BlobContainerClient(new Uri($"{FileStorageUrl}{FileStorageContainer}"), new DefaultAzureCredential(options));
                            var blob = blobClient.GetBlobClient(fileName);
                            //Check if the blob was already process
                            //file names must be unique
                            var Exists = await blob.ExistsAsync();
                            if (!Exists)
                            {
                                await blob.UploadAsync(openFileStream);
                            }
                        }
                    }
                }
            }
            
            string result = JsonConvert.SerializeObject(new { files = string.Join<string>(",", files) });
            await messageActions.CompleteMessageAsync(message);
            return result;


        }
        
    }
}
