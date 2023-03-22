using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace src
{
    internal class Constants
    {
        public const string MapperTaskExecutable = "MapperTask.exe";
        public const string ReducerTaskExecutable = "ReducerTask.exe";
        public const string ReducerTaskResultBlobName = "ReducerTaskOutput";

        public const string MapperTaskPrefix = "MapperTask";
        public const string ReducerTaskId = "ReducerTask";
        public const string TextFilePath = "Text.txt";

        /// <summary>
        /// The list of required files to run the sample executables.  Since the JobManager.exe is run as a job manager in Batch 
        /// it needs all the DLLs of the Batch client library.
        /// This is uploaded as an application that is installed on the node and shared by the tasks. 
        /// Leaving this here to understand what files are needed in the application package for this sample POC
        /// </summary>
        public readonly static IReadOnlyList<string> RequiredExecutableFiles = new List<string>
            {
                "JobSubmitter.pdb",
                MapperTaskExecutable,
                MapperTaskExecutable + ".config",
                "MapperTask.pdb",
                ReducerTaskExecutable,
                ReducerTaskExecutable + ".config",
                "ReducerTask.pdb",
                "settings.json",
                "accountsettings.json",
                "Microsoft.Azure.Batch.Samples.Common.dll",
                "Common.dll",
                "Microsoft.WindowsAzure.Storage.dll",
                "Microsoft.Azure.Batch.dll",
                "Microsoft.Rest.ClientRuntime.dll",
                "Microsoft.Rest.ClientRuntime.Azure.dll",
                "Newtonsoft.Json.dll",
                "Microsoft.Extensions.Configuration.dll",
                "Microsoft.Extensions.Configuration.Abstractions.dll",
                "Microsoft.Extensions.Configuration.Json.dll",
                "Microsoft.Extensions.Configuration.Binder.dll",
                "Microsoft.Extensions.Configuration.FileExtensions.dll",
                "Microsoft.Extensions.FileProviders.Physical.dll",
                "Microsoft.Extensions.FileProviders.Abstractions.dll",
                //"netstandard.dll",
                "Microsoft.Extensions.Primitives.dll",
                //"System.Net.Http.dll",
               "Azure.Storage.Files.DataLake.dll",
               "Azure.Identity.dll",
               "Azure.Core.dll",
               "Microsoft.Identity.Client.dll",
               "Microsoft.Identity.Client.Extensions.Msal.dll",
              // "Microsoft.IdentityModel.Abstractions.dll",
               "Microsoft.Bcl.AsyncInterfaces.dll",
               "Microsoft.Azure.KeyVault.Core.dll",
               "Azure.Storage.Common.dll",
               "Azure.Storage.Blobs.dll",
                "Microsoft.Azure.Batch.FileStaging.dll",
                "Microsoft.Extensions.FileSystemGlobbing.dll",
                "System.Buffers.dll",
                "System.Diagnostics.DiagnosticSource.dll",
                "System.IO.Hashing.dll",
                "System.Memory.Data.dll",
                "System.Memory.dll",
                "System.Numerics.Vectors.dll",
                "System.Runtime.CompilerServices.Unsafe.dll",
                "System.Security.Cryptography.ProtectedData.dll",
                "System.ValueTuple.dll",
               "System.Threading.Tasks.Extensions.dll",
               "System.Text.Json.dll",
                "System.Text.Encodings.Web.dll"


            };
    }
}
