using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using System.Runtime.CompilerServices;
using System.Numerics;
using Azure.Identity;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace MyCompany.Function
{
    /// <summary>
    /// Count the lines of the file to split into multiple files evenly
    /// </summary>
    public static class CountLinesFunc
    {
        [FunctionName("CountLinesFunc")]
        [return: ServiceBus("linequeue", Connection = "ServiceBusConnectionAppSetting")]
        public static async Task<string> RunAsync(
            [ServiceBusTrigger("filequeue", Connection = "ServiceBusConnectionAppSetting")] ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions,
            ILogger log)
        {
            string myQueueItem = Encoding.UTF8.GetString(message.Body);
            log.LogInformation($"C# HTTP trigger CountLines function processed a request.{myQueueItem}");

            //TODO: Error handling for environment variables are set
            string userAssignedClientId = Environment.GetEnvironmentVariable("User_Assigned_Managed_Identity_ClientID");
            int taskNumber = Convert.ToInt32(Environment.GetEnvironmentVariable("Task_Number"));
            string url = myQueueItem;
            int linesPerFile = 0;
            var options = new DefaultAzureCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzureGovernment, ManagedIdentityClientId = userAssignedClientId };

            //Read File to count lines and determine number of lines for each file
            using (StreamReader streamReader = new StreamReader(new BlobClient(new Uri(url), new DefaultAzureCredential(options)).OpenRead(null)))
            {
                int lineCount = 0;
                while (!streamReader.EndOfStream)
                {
                    ++lineCount;
                    await streamReader.ReadLineAsync();
                }
                log.LogInformation($"Total lines: {lineCount}");

                //Compute the number of lines per file.
                linesPerFile = lineCount / taskNumber;
            }

            string result = JsonConvert.SerializeObject(new { url = url, count = linesPerFile });
            await messageActions.CompleteMessageAsync(message);
            return result;
        }
    }
}
