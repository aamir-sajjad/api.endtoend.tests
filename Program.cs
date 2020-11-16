using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using WindSim.Common.DataLoaders.DataLoaders;
using WindSim.Common.Model.Processing;

namespace Core.API.EndToEnd.Tests
{
    class Program
    {
        public static IConfiguration Configuration { get; } = new ConfigurationBuilder()
           .SetBasePath(Directory.GetCurrentDirectory())
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddEnvironmentVariables()
           .Build();

        public static Serilog.ILogger Logger { get; } = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Seq(@"http://51.138.23.89")
            .CreateLogger();

        static async Task Main(string[] args)
        {
            Log.Logger = Logger;

            try
            {

                Console.WriteLine("Test Application Starting...");
                Log.Information("Core API EndToEnd Test Application Starting...");

                #region input from args

                if (args.Length == 0 || args.Length < 3)
                {
                    Console.WriteLine("Please provide 1:project id(GUID), 2:source path and 3:destination path.");
                    Environment.Exit(1);
                }

                var projectId = args[0];
                var sourcePath = args[1];
                var destinationPath = args[2];

                Console.WriteLine($"Input paramerters are 1:project id {projectId} 2:source path {sourcePath} 3:destination path {destinationPath}");

                #endregion input from args

                #region Experimental Code

                //Console.WriteLine("Please enter the project id");
                //var projectId = Console.ReadLine();
                //Console.WriteLine($"project id {projectId}");

                //Console.WriteLine("Please enter the source path");
                //var sourcePath = Console.ReadLine();
                //Console.WriteLine($"source path {sourcePath}");

                //Console.WriteLine("Please enter the detination path");
                //var destinationPath = Console.ReadLine();
                //Console.WriteLine($"detination path {destinationPath}");

                // copy the the file from source folder and place it in desitnation folder
                // source path: D:\Project\CoreAPIEndToEndTests\Core.API.EndToEnd.Tests\SEWPGProjectInput
                // destination path: D:\Temp\08-11-20

                //CopyDirectory(@sourcePath, @destinationPath);

                //if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                //{
                //    Environment.Exit(1);
                //}

                #endregion Expermintal Code

                #region Cloud Job

                var services = ConfigureServices();
                var serviceProvider = services.BuildServiceProvider();

                var cfdRansTests = serviceProvider.GetService<ICFDRansTests>();
                cfdRansTests.SourcePath = sourcePath;
                cfdRansTests.DestinationPath = destinationPath;

                // upload the project input
                await cfdRansTests.UploadCFDRansInput(new Guid(projectId), sourcePath);
                // start the job
                var accessToken = await cfdRansTests.SubmitCFDJob(new Guid(projectId));

                // receive progress status, and based on progess status download the output
                //await cfdRansTests.ConnectToJobNotificationHub(accessToken, projectId.ToString());
                await Task.Delay(1800);
                await cfdRansTests.CheckStatusInInterval(projectId);

                #endregion Cloud Job

                Console.WriteLine("Press Enter key to exit.");

                Console.WriteLine("End of Test Application!");

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Core API EndToEnd Exception...!");
                Console.WriteLine($"Main: {ex.Message}");
                Log.Error(ex, "Core API EndToEnd Exception {message}", ex.Message);
                await Task.Delay(1800);
                Environment.Exit(1);
            }

            await Task.CompletedTask;
            Environment.Exit(0);
        }
        private static IServiceCollection ConfigureServices()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddSingleton(Configuration);
            services.AddSingleton<ICFDRansTests, CFDRansTests>();
            services.AddSingleton<IDataLoader, BlobStorageDataLoader>();
            services.AddHttpClient<ICFDRansTests, CFDRansTests>(client =>
            {
                client.BaseAddress = new Uri(@"https://sewpg-api.azurewebsites.net/");
            })
            .AddPolicyHandler(GetRetryPolicy());

            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddSerilog(logger: Logger, dispose: true);
            });

            return services;
        }

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
                .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }


        public static void CopyDirectory(string sourcePath, string targetPath, bool overwriteExistingFiles = true)
        {
            var dir = new DirectoryInfo(sourcePath);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            var files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                var targetFilePath = Path.Combine(targetPath, file.Name);
                if (overwriteExistingFiles || File.Exists(targetFilePath) == false)
                {
                    file.CopyTo(targetFilePath, true);
                }
            }

            foreach (var subDirectory in dir.GetDirectories())
            {
                var targetSubDirectory = Path.Combine(targetPath, subDirectory.Name);
                CopyDirectory(subDirectory.FullName, targetSubDirectory, overwriteExistingFiles);
            }
        }


        //end of class
    }
}
