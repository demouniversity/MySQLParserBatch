using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Batch;
using Microsoft.Azure.Batch.Auth;
using Microsoft.Azure.Batch.Common;
using Microsoft.Azure.Batch.Protocol.Models;
using Microsoft.Azure.Management.Batch;
using Microsoft.Azure.Management.Batch.Models;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json;
using ApplicationPackageReference = Microsoft.Azure.Batch.ApplicationPackageReference;
using AutoPoolSpecification = Microsoft.Azure.Batch.AutoPoolSpecification;
using BatchPoolIdentity = Microsoft.Azure.Batch.BatchPoolIdentity;
using CloudJob = Microsoft.Azure.Batch.CloudJob;
using CloudPool = Microsoft.Azure.Batch.CloudPool;
using ComputeNode = Microsoft.Azure.Batch.ComputeNode;
using ComputeNodeState = Microsoft.Azure.Batch.Common.ComputeNodeState;
using ImageReference = Microsoft.Azure.Batch.ImageReference;
using PoolIdentityType = Microsoft.Azure.Batch.Common.PoolIdentityType;
using PoolInformation = Microsoft.Azure.Batch.PoolInformation;
using PoolLifetimeOption = Microsoft.Azure.Batch.Common.PoolLifetimeOption;
using PoolNodeCounts = Microsoft.Azure.Batch.PoolNodeCounts;
using PoolSpecification = Microsoft.Azure.Batch.PoolSpecification;
using VirtualMachineConfiguration = Microsoft.Azure.Batch.VirtualMachineConfiguration;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace src
{

    public class NodePoolCheckFunc
    {
        //Pull all settings from the Application Settings
        private string BatchBaseUrl = Environment.GetEnvironmentVariable("Batch_Base_Url");
        private string BatchAccountName = Environment.GetEnvironmentVariable("Batch_Account_Name");
        private string BatchKey = Environment.GetEnvironmentVariable("Batch_Key");
        private string VM_SIZE = Environment.GetEnvironmentVariable("VM_Size");
        private string ApplicationPackageName = Environment.GetEnvironmentVariable("Application_Package_Name");
        private string ApplicationPackageVersion = Environment.GetEnvironmentVariable("Application_Package_Version");
        private string ApplicationPackageID = Environment.GetEnvironmentVariable("Application_PackageID");
        private string userAssignedClientId = Environment.GetEnvironmentVariable("User_Assigned_Managed_Identity_ClientID");
        private string BatchManagementEndpoint = Environment.GetEnvironmentVariable("Batch_Management_Endpoint");
        private string SubscriptionID = Environment.GetEnvironmentVariable("SubscriptionID");
        private string ResourceGroupName = Environment.GetEnvironmentVariable("Resource_Group_Name");
        private int taskNumber = Convert.ToInt32(Environment.GetEnvironmentVariable("Task_Number"));
        private string UserAssignedManagedIdentityID = Environment.GetEnvironmentVariable("User_Assigned_Managed_IdentityID");
        //TODO Settings and Key Vault
        /// <summary>
        /// Create or obtain and existing Node Pool
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageActions"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [return: ServiceBus("batchqueue", Connection = "ServiceBusConnectionAppSetting")]
        [FunctionName("NodePoolCheckFunc")]
        public async Task<string> Run([ServiceBusTrigger("nodequeue", Connection = "ServiceBusConnectionAppSetting")] ServiceBusReceivedMessage message,
    ServiceBusMessageActions messageActions, ILogger log)
        {
            string myQueueItem = Encoding.UTF8.GetString(message.Body);
            string uami = UserAssignedManagedIdentityID;
            //TODO: Error handling and Check environmental variables for existing pool
            log.LogInformation($"NodePoolCheckFunc ServiceBus queue trigger function processed message: {myQueueItem}");
            string result = String.Empty;
            string id = String.Empty;
            
            string PoolPrefix = "TextSearchPool";
            string PoolId = $"{PoolPrefix}{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}";
            dynamic json = JsonConvert.DeserializeObject(myQueueItem);

            //Allow enough compute nodes in the pool to run each mapper task
            int numberOfPoolComputeNodes = taskNumber;

            //Set up the Batch Service credentials used to authenticate with the Batch Service.
            BatchSharedKeyCredentials credentials = new BatchSharedKeyCredentials(BatchBaseUrl, BatchAccountName, BatchKey);

            using (BatchClient batchClient = BatchClient.Open(credentials))
            {
                //Search for existing node pool that has the # of tasks/nodes that are usable. 
                IPagedEnumerable<CloudPool> temppools = batchClient.PoolOperations.ListPools(new ODATADetailLevel(
                    filterClause: $"startswith(id, '{PoolPrefix}')",
                    selectClause: "id,state"));
                CloudPool found = temppools.Where<CloudPool>(p => (p.ListComputeNodes().Where(t => t.State != ComputeNodeState.Unusable)?.Count()) == taskNumber).FirstOrDefault();

                //Node Pool found and will return the existing pool. This will decrease time since a new pool is not needed. 
                if (found != null)
                {
                    log.LogInformation($"Using Pool {found.Id}");
                    id = found.Id;
                    result = JsonConvert.SerializeObject(new { files = json.files, jobId = "", poolId = id });
                    return result;//return when an existing pool is found and is usable
                }
                
                //Define pool with User Assigned Managed Identity 
                var poolParameters = new Pool(name: $"{PoolId}")
                {
                    VmSize = VM_SIZE,
                    ScaleSettings = new ScaleSettings
                    {
                        FixedScale = new FixedScaleSettings
                        {
                            TargetDedicatedNodes = numberOfPoolComputeNodes
                        }
                    },
                    
                    DeploymentConfiguration = new DeploymentConfiguration
                    {
                        VirtualMachineConfiguration = new Microsoft.Azure.Management.Batch.Models.VirtualMachineConfiguration(new
                        Microsoft.Azure.Management.Batch.Models.ImageReference(
                                        publisher: "microsoftwindowsserver",
                                        offer: "windowsserver",
                                        sku: "2022-datacenter",
                                        version: "latest"),
                                        nodeAgentSkuId: "batch.node.windows amd64")
                    },
                    Identity = new Microsoft.Azure.Management.Batch.Models.BatchPoolIdentity
                    {
                        Type = Microsoft.Azure.Management.Batch.Models.PoolIdentityType.UserAssigned,
                        UserAssignedIdentities = new Dictionary<string, UserAssignedIdentities>
                        {
                            [$"{uami}"] = new UserAssignedIdentities()
                        }
                    },
                    ApplicationPackages = new List<Microsoft.Azure.Management.Batch.Models.ApplicationPackageReference>
                    {
                        new Microsoft.Azure.Management.Batch.Models.ApplicationPackageReference
                        {
                            Id = ApplicationPackageID,
                            Version = ApplicationPackageVersion
                        }
                    }

                };
                
                //string accessToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.usgovcloudapi.net/")
                var options = new DefaultAzureCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzureGovernment, ManagedIdentityClientId = userAssignedClientId };
                var credential = new DefaultAzureCredential(options);
                
                var token = credential.GetToken(new Azure.Core.TokenRequestContext(new[] { BatchManagementEndpoint }));
             
                log.LogDebug($"Received token for authentication: {token.Token}");
                using (BatchManagementClient managementClient = new BatchManagementClient(new TokenCredentials(token.Token)))
                {
                    managementClient.BaseUri = new Uri(BatchManagementEndpoint);
                    managementClient.SubscriptionId = SubscriptionID;
                    try
                    {
                        log.LogInformation("Creating Node Pool");
                        //Create the pool
                        var pool = managementClient.Pool.CreateWithHttpMessagesAsync(
                        poolName: $"{PoolId}",
                        resourceGroupName: ResourceGroupName,
                        accountName: BatchAccountName,
                        parameters: poolParameters,
                        cancellationToken: default(CancellationToken)).ConfigureAwait(false);
                        
                        var test = pool.GetAwaiter().GetResult();
                        if (test.Response.IsSuccessStatusCode)
                        {
                            //Complete the message, if only successful
                            await messageActions.CompleteMessageAsync(message);
                        }
                        else
                        {
                            //TODO
                            //Send a null value to the next function, but will receive the next message to try again.
                            return JsonConvert.SerializeObject(new { files = json.files, jobId = "", poolId = "" });
                        }

                    }
                    catch(Exception ex)
                    {
                        log.LogError($"Exception with creating Node: {ex.Message} {ex.InnerException?.Message}");
                        return JsonConvert.SerializeObject(new { files = json.files, jobId = "", poolId = "" });
                    }
                }

            }

            result = JsonConvert.SerializeObject(new { files = json.files, jobId = "", poolId = $"{PoolId}" });
            
            
            return result;
        }

    }
}
