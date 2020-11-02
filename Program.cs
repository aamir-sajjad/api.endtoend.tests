using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
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

        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Test Application Starting...");
                var services = ConfigureServices();
                var serviceProvider = services.BuildServiceProvider();
                var projectId = new Guid("44033fa4-465d-401d-b6cf-0b292498bbd1");
                var cfdRansTests = serviceProvider.GetService<ICFDRansTests>();

                //upload the project input
                await cfdRansTests.UploadProjectInput(projectId);
                //start the job
                var accessToken = await cfdRansTests.SubmitJob(projectId);

                //receive progress status, and based on progess status download the output
                await cfdRansTests.ConnectToJobNotificationHub(accessToken, projectId.ToString());

                Console.WriteLine("Press enter to to unsubscribe from jobs progress updates and to end the test");
                Console.ReadLine();
                Console.WriteLine("End of Test Application!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Core Exception...!");
                Console.WriteLine($"Main: {ex.Message}");
            }
            Console.ReadLine();
        }
        private static IServiceCollection ConfigureServices()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddSingleton(Configuration);
            services.AddSingleton<ICFDRansTests, CFDRansTests>();
            services.AddSingleton<IDataLoader, BlobStorageDataLoader>();
            services.AddHttpClient<ICFDRansTests, CFDRansTests>(client =>
            {
                client.BaseAddress = new Uri(Configuration["WindSim:ApplicationBaseUrl"]);
            })
            .AddPolicyHandler(GetRetryPolicy());


            return services;
        }

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
                .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }



        //end of class
    }
}
