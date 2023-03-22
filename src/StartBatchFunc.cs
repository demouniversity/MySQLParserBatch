using Microsoft.Azure.Batch;
using Microsoft.Azure.Batch.Auth;
using Microsoft.Azure.Batch.Common;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace src
{
    public class StartBatchFunc
    {
        //Pull all values from App Settings - TODO: Add Error Handling
        private string BatchBaseUrl = Environment.GetEnvironmentVariable("Batch_Base_Url");
        private string BatchAccountName = Environment.GetEnvironmentVariable("Batch_Account_Name");
        private string BatchKey = Environment.GetEnvironmentVariable("Batch_Key");
        private string FileStorageUrl = Environment.GetEnvironmentVariable("File_Storage_Url");
        private string ApplicationPackageName = Environment.GetEnvironmentVariable("Application_Package_Name");
        private string ApplicationPackageVersion = Environment.GetEnvironmentVariable("Application_Package_Version");
        private int JobFileRetentionHours = Convert.ToInt32(Environment.GetEnvironmentVariable("Job_File_Retention_Hours"));
        private string FileStorageContainer = Environment.GetEnvironmentVariable("File_Storage_Container");
        private string UserAssignedManagedIdentityID = Environment.GetEnvironmentVariable("User_Assigned_Managed_IdentityID");
        int taskNumber = Convert.ToInt32(Environment.GetEnvironmentVariable("Task_Number"));
        string inputcontainer = Environment.GetEnvironmentVariable("Batch_Input_Container_Url");
        string outputcontainer = Environment.GetEnvironmentVariable("Batch_Output_Container_Url");
        private string jobId = String.Empty;
 

        /// <summary>
        /// Creates the Job and the Task that will convert the split files into csv files
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageActions"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("StartBatchFunc")]
        public async Task Run([ServiceBusTrigger("batchqueue", Connection = "ServiceBusConnectionAppSetting")] ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
            ILogger log)
        {
            //TODO: Error handling with message.Body
            if(message == null)
            {
                log.LogError("No message received for StartBatchFunc");
                return;
            }
            string myQueueItem = Encoding.UTF8.GetString(message.Body);
            if(String.IsNullOrEmpty(myQueueItem))
            {
                log.LogError("Message body is empty. Unable to create a job");
                return;
            }
            log.LogInformation($"StartBatchFunc ServiceBus queue trigger function processed message: {myQueueItem}");

            dynamic json = JsonConvert.DeserializeObject(myQueueItem);
            string filesstr = json.files;
            if(String.IsNullOrEmpty(filesstr) ) { 
                log.LogError("No file names received");
                return;

            }
            List<string> files = filesstr.Split(',').ToList<string>();
            
            CloudJob unboundJob = null;
            string job = json.jobId;
            string poolId = json.poolId;
            jobId = String.Empty;
            //Check if job will match the file name and will not run with duplicate names
            //assumes <filename>.sql_0.txt format
            jobId = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(files[0]));

            //Set up the Batch Service credentials used to authenticate with the Batch Service.
            BatchSharedKeyCredentials credentials = new BatchSharedKeyCredentials(BatchBaseUrl, BatchAccountName, BatchKey);
            
            using (BatchClient batchClient = BatchClient.Open(credentials))
            {
               
                //There is a node pool and will create a job
                if (!String.IsNullOrEmpty(poolId) )
                {
                    //Search for Job example:
                    //Below are 2 examples on how to query for an existing job. 
                    //ODATADetailLevel detail = new ODATADetailLevel(filterClause: string.Format("id eq '{0}'", jobId));
                    //unboundJob = (await batchClient.JobOperations.ListJobs(detailLevel: detail).ToListAsync().ConfigureAwait(continueOnCapturedContext: false)).FirstOrDefault();
                    //unboundJob = batchClient.JobOperations.ListJobs(new ODATADetailLevel(selectClause: "id,state")).Where<CloudJob>(x => x.Id.StartsWith("jobtext" + DateTime.UtcNow.ToString("yyyyMMdd"))).FirstOrDefault();
                    unboundJob = batchClient.JobOperations.ListJobs(new ODATADetailLevel(selectClause: "id,state")).Where<CloudJob>(x => x.Id == (jobId)).FirstOrDefault();
                    // Checking if there is a job with this filename and we have already proccessed this file
                    //SB will send multiple messages for this job, which we do not want to create more jobs then needed. 
                    //May want to add to this and determine if the existing job executed successfully
                    if (unboundJob != null) {
                        log.LogInformation($"Job {unboundJob.Id} for the file {files[0]} has already been processed. Delete the existing job and run this again.");
                        return; 
                    }
                    log.LogInformation($"Creating job: {jobId}");
                    unboundJob = batchClient.JobOperations.CreateJob(jobId, new PoolInformation { PoolId = $"{poolId}" }); //TODO TEST ASYNC
                    unboundJob.UsesTaskDependencies = true;
                    unboundJob.Commit();//TODO TEST ASYNC

                }
                else//may add logic to call the node pool function again and then create the job again
                {
                    log.LogError("No Pool to create a job");
                    return;
                }

                log.LogInformation($"Creating Mapper tasks");
                // Add tasks to the job
                ////// Job ID container
                var mapperTasks = CreateMapperTasks($"{inputcontainer}/{jobId}", $"{outputcontainer}/{jobId}", files, log);
                log.LogInformation("Creating reducer task");
                var reducerTask = CreateReducerTask($"{inputcontainer}/{jobId}", $"{outputcontainer}/{jobId}", jobId, mapperTasks, log);

                var tasksToAdd = Enumerable.Concat(mapperTasks, new[] { reducerTask });

                //Submit the unbound task collection to the Batch Service.
                //Use the AddTask method which takes a collection of CloudTasks for the best performance.
                log.LogInformation($"Submitting {taskNumber} mapper tasks");
                log.LogInformation($"Submitting 1 reducer task for job {jobId}");
                await batchClient.JobOperations.AddTaskAsync(jobId, tasksToAdd);

                //An object which is backed by a corresponding Batch Service object is "bound."
                log.LogInformation($"Obtaining job number {jobId}");
                CloudJob boundJob = await batchClient.JobOperations.GetJobAsync(jobId);
                log.LogInformation($"Obtaining bound job number {boundJob.Id}");
                // Update the job now that we've added tasks so that when all of the tasks which we have added
                // are complete, the job will automatically move to the completed state.
                boundJob.OnAllTasksComplete = OnAllTasksComplete.TerminateJob;
                boundJob.Commit();//TODO TEST ASYNC
                boundJob.Refresh();//TODO TEST ASYNC

                //
                // Wait for the tasks to complete.
                //
                List<CloudTask> tasks = await batchClient.JobOperations.ListTasks(jobId).ToListAsync();
                TimeSpan maxJobCompletionTimeout = TimeSpan.FromMinutes(60);

                // Monitor the current tasks to see when they are done.
                // Occasionally a task may get killed and requeued during an upgrade or hardware failure, 
                // Robustness against this was not added into the sample for 
                // simplicity, but should be added into any production code.
                log.LogInformation("Waiting for job's tasks to complete");

                //Code below will monitor the tasks, since function apps are short live, we do not include. 
                //Will leave the monitor to logging on the Batch service and keep this code if want to add later
               /* TaskStateMonitor taskStateMonitor = batchClient.Utilities.CreateTaskStateMonitor();
                try
                {
                    await taskStateMonitor.WhenAll(tasks, TaskState.Completed, maxJobCompletionTimeout);
                }
                finally
                {
                    log.LogInformation("Done waiting for all tasks to complete");

                    // Refresh the task list
                    tasks = await batchClient.JobOperations.ListTasks(jobId).ToListAsync();

                    //Check to ensure the job manager task exited successfully.
                    foreach (var task in tasks)
                    {
                        await CheckForTaskSuccessAsync(task, dumpStandardOutOnTaskSuccess: false);
                    }
                }*/

            }
            //await messageActions.CompleteMessageAsync(message);
        }

        private IEnumerable<CloudTask> CreateMapperTasks(string inputContainerSas, string outputContainerSas, IList<string> list, ILogger log)
        {
            //The collection of tasks to add to the Batch Service.
            List<CloudTask> tasksToAdd = new List<CloudTask>();
            string[] files = list.ToArray<string>();

            for (int i = 0; i < taskNumber; i++)
            {
                string taskId = GetTaskId(i);

                log.LogInformation($"Creating task: {taskId}");

                //files from the queue
                string fileBlobName = files[i];

                //execute the mapper task from the application files 
                //Package need to be set as default in Azure Batch Service
                string commandLine = string.Format(@"cmd /c %AZ_BATCH_APP_PACKAGE_" + ApplicationPackageName.ToUpper() + @"%\{0} %AZ_BATCH_TASK_WORKING_DIR%\{1} 2>> rerror.log", Constants.MapperTaskExecutable, fileBlobName);
                CloudTask unboundMapperTask = new CloudTask(taskId, commandLine);

                unboundMapperTask.ApplicationPackageReferences = new List<ApplicationPackageReference>
                {
                    new ApplicationPackageReference
                    {
                        ApplicationId = ApplicationPackageName,
                        Version = ApplicationPackageVersion
                    }
                };
                //Amount of time to keep the files on the nodes after the job has completed
                //Currently we are only concerned about hours
                TaskConstraints taskConstraints = new TaskConstraints();
                taskConstraints.RetentionTime = new TimeSpan(JobFileRetentionHours, 0, 0);
                unboundMapperTask.Constraints = taskConstraints;
                unboundMapperTask.UserIdentity = new UserIdentity(new AutoUserSpecification(elevationLevel: ElevationLevel.Admin));

                Uri accountUri = new Uri($"{FileStorageUrl}");
                CloudBlobClient blobClient = new CloudBlobClient(accountUri);
                CloudBlobContainer container = blobClient.GetContainerReference($"{FileStorageContainer}");

                log.LogInformation($"Get file from container: {fileBlobName}");

                List<ResourceFile> mapperTaskResourceFiles = new List<ResourceFile>();

                log.LogInformation($"Input container for Mapper Task: {container.Uri.ToString()}/{fileBlobName}");//TODO:MAY Need to update to jobid location

                //input files from the split files. Assign each file to each task
                mapperTaskResourceFiles.Add(ResourceFile.FromUrl($"{container.Uri.ToString()}/{fileBlobName}",
                    identityReference: new ComputeNodeIdentityReference() { ResourceId = UserAssignedManagedIdentityID }
                  , filePath: fileBlobName));

                //output to the linked input containerfor input to the reducer task
                unboundMapperTask.OutputFiles = new List<OutputFile>
                {
                    new OutputFile(
                        filePattern: @"..\stdout.txt",
                        destination: new OutputFileDestination(
                            container: new OutputFileBlobContainerDestination(inputContainerSas,
                            identityReference: new ComputeNodeIdentityReference() { ResourceId = UserAssignedManagedIdentityID },
                                path: taskId)),
                        uploadOptions: new OutputFileUploadOptions(uploadCondition: OutputFileUploadCondition.TaskSuccess))
                };
                unboundMapperTask.ResourceFiles = mapperTaskResourceFiles;

                log.LogInformation($"Completed Task Setup for file {taskId} that output to outputcontainer {outputContainerSas}");

                yield return unboundMapperTask;
            }
        }

        private static string GetTaskId(int i)
        {
            return $"MapperTask_{i}";
        }

        private CloudTask CreateReducerTask(string inputContainerSas, string outputContainerSas, string dir, IEnumerable<CloudTask> mapperTasks, ILogger log)
        {
            log.LogInformation($"Creating reducer tasks {inputContainerSas}");

            //Execute the reducer task from the application location. Application has to be set to default 
            //to be copied to the nodes
            string commandLine = string.Format(@"cmd /c %AZ_BATCH_APP_PACKAGE_" + ApplicationPackageName.ToUpper() + @"%\{0} {1} {2} 2>> rerror.log", Constants.ReducerTaskExecutable, taskNumber, dir);
            CloudTask unboundReducerTask = new CloudTask(Constants.ReducerTaskId, commandLine);

            //The set of files (exes, dlls and configuration files) required to run the reducer task.
            List<ResourceFile> reducerTaskResourceFiles = new List<ResourceFile>(); 

            //The mapper outputs to reduce
            var mapperOutputs = Enumerable.Range(0, taskNumber).Select(p => GetTaskId(p));
            log.LogInformation($"Numper of mapper ouputs: {mapperOutputs.Count()}");

            //get all files from the mapper tasks to convert to csv files
            reducerTaskResourceFiles.Add(ResourceFile.FromStorageContainerUrl(inputContainerSas,
                       identityReference: new ComputeNodeIdentityReference() { ResourceId = UserAssignedManagedIdentityID }
                  /* , filePath: fileBlobName*/));
            
            unboundReducerTask.ResourceFiles = reducerTaskResourceFiles;
            // Upload the reducer task files as the result file for the entire job
            unboundReducerTask.OutputFiles = new List<OutputFile>
            {
                new OutputFile(
                    filePattern: @"*.csv",//stdout.txt",
                    destination: new OutputFileDestination(
                        container: new OutputFileBlobContainerDestination(outputContainerSas +"/", 
                        identityReference: new ComputeNodeIdentityReference() { ResourceId = UserAssignedManagedIdentityID }
                        /*path: Constants.ReducerTaskResultBlobName*/)),
                    uploadOptions: new OutputFileUploadOptions(uploadCondition: OutputFileUploadCondition.TaskSuccess))
            };

            // Depend on the mapper tasks so that they are all complete before the reducer runs
            unboundReducerTask.DependsOn = TaskDependencies.OnTasks(mapperTasks);
            log.LogInformation($"Completed Reducer Task Setup for task to output storage {outputContainerSas}");
            return unboundReducerTask;
        }


    }


}
