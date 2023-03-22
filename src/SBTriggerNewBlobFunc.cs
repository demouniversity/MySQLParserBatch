using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Newtonsoft.Json;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace MyCompany.Function
{
    public class SBTriggerNewBlobFunc
    {
        /// <summary>
        /// This just a kick-off function that will pull the blob information that trigger the created event
        /// </summary>
        /// <param name="myQueueItem">Contains the url of the blob that triggered the function</param>
        /// <param name="log"></param>
        /// <returns></returns>
        [return: ServiceBus("filequeue", Connection = "ServiceBusConnectionAppSetting")]
        [FunctionName("SBTriggerNewBlobFunc")] 
        public async Task<string> RunAsync([ServiceBusTrigger("outqueue", Connection = "ServiceBusConnectionAppSetting")] ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        ILogger log)
        {
            string myQueueItem = Encoding.UTF8.GetString(message.Body);
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
            dynamic json = JsonConvert.DeserializeObject(myQueueItem);
            await messageActions.CompleteMessageAsync(message);
            return json.data.url;
        }
    }
}
