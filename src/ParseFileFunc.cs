using System;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace src
{
    public class ParseFileFunc
    {
        /// <summary>
        /// Start the Parse File process, this can act as function to obtain the current process
        /// This current implemention will send the message to start parsing the files. 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageActions"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [return: ServiceBus("nodequeue", Connection = "ServiceBusConnectionAppSetting")]
        [FunctionName("ParseFileFunc")]
        public async Task<string> Run([ServiceBusTrigger("parsefilequeue", Connection = "ServiceBusConnectionAppSetting")] ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions, 
        ILogger log)
        {
            string myQueueItem = Encoding.UTF8.GetString(message.Body);
            //Alternative methods: Change this to Async Request Pattern, right now driven by Service Bus
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
            await messageActions.CompleteMessageAsync(message);
            return myQueueItem;
        }
    }
}
