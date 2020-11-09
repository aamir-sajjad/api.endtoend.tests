using Core.API.Common;
using Core.API.Hubs.Model;
using Core.API.TestHelper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Polly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindSim.Common.Model.Jobs;
using WindSim.Common.Model.Processing;

namespace Core.API.EndToEnd.Tests
{
    public interface ICFDRansTests
    {
        string SourcePath
        {
            get;
            set;
        }
        string DestinationPath
        {
            get;
            set;
        }
        Task UploadCFDRansInput(Guid projectId, string srcPath);
        Task UploadSynthesisInput(Guid projectId, string srcPath);
        Task UploadAEPInput(Guid projectId, string srcPath);
        Task<string> SubmitCFDJob(Guid projectId);
        Task<HubConnection> ConnectToJobNotificationHub(string accessToken, string projectId);
        Task<List<JobsStatusViewModel>> GetProjectStatus(string projectId);
    }

    public class CFDRansTests : ICFDRansTests
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IDataLoader _dataLoader;
        private string accessToken;
        public CFDRansTests(HttpClient httpClient, IConfiguration configuration, IDataLoader dataLoader)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromMinutes(20);
            accessToken = AuthenticationHelper.GetToken(_httpClient).GetAwaiter().GetResult();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _configuration = configuration;
            _dataLoader = dataLoader;
        }

        // cfd job
        public async Task<string> SubmitCFDJob(Guid projectId)
        {
            try
            {
                Console.WriteLine("SubmitCFDJob: at the start of SubmitJob");
                var timer = new Stopwatch();
                timer.Restart();
                timer.Start();

                var model = new CFDRansSubmitJobModel { ProjectId = projectId };
                var content = JsonSerializer.Serialize(model);
                HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("api/CFDrans/SubmitJob", httpContent);

                response.EnsureSuccessStatusCode();


                Console.WriteLine($"SubmitCFDJob: at the end of SubmitJob took minutes: {timer.Elapsed.TotalMinutes}");
                return accessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitCFDJob: {ex.Message}");
                throw ex;
            }
        }

        // synthesis job
        public async Task<string> SubmitSynthesisJob(Guid projectId)
        {
            try
            {
                Console.WriteLine("SubmitSynthesisJob: at the start of SubmitJob");
                var timer = new Stopwatch();
                timer.Restart();
                timer.Start();

                var model = new SynthesisSubmitJobModel { ProjectId = projectId };
                var content = JsonSerializer.Serialize(model);
                HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("api/Synthesis/SubmitJob", httpContent);

                response.EnsureSuccessStatusCode();


                Console.WriteLine($"SubmitSynthesisJob: at the end of SubmitJob took minutes: {timer.Elapsed.TotalMinutes}");
                return accessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitSynthesisJob: {ex.Message}");
                throw ex;
            }
        }

        // aep job
        public async Task<string> SubmitAEPJob(Guid projectId)
        {
            try
            {
                Console.WriteLine("SubmitAEPJob: at the start of SubmitJob");
                var timer = new Stopwatch();
                timer.Restart();
                timer.Start();

                var model = new AEPSubmitJobModel { ProjectId = projectId };
                var content = JsonSerializer.Serialize(model);
                HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("api/AEP/SubmitJob", httpContent);

                response.EnsureSuccessStatusCode();

                Console.WriteLine($"SubmitAEPJob: at the end of SubmitJob took minutes: {timer.Elapsed.TotalMinutes}");
                return accessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitAEPJob: {ex.Message}");
                throw ex;
            }
        }

        public async Task<HubConnection> ConnectToJobNotificationHub(string accessToken, string projectId)
        {
            Console.WriteLine("ConnectToJobNotificationHub: at the start of ConnectToJobNotificationHub");
            try
            {
                var timer = new Stopwatch();
                timer.Restart();
                timer.Start();

                var baseUrl = _configuration["WindSim:ApplicationBaseUrl"];
                Console.WriteLine($"baseUrl {baseUrl}");
                var hubUrl = $"{baseUrl}/MessageHub?projectId={projectId}";
                var hubConnection = new HubConnectionBuilder()
                  .WithUrl(new Uri(hubUrl), options =>
                  {
                      options.AccessTokenProvider = () => Task.FromResult(accessToken);
                  }
                  ).WithAutomaticReconnect()
                  .Build();
                await hubConnection.StartAsync();

                hubConnection.On<JobNotificationResponse>("MessageReceived", (message) =>
                {
                    if (string.IsNullOrEmpty(message.JobProgressStatusMessage))
                    {
                        var projectJobsStatus = GetProjectStatus(projectId).GetAwaiter().GetResult();

                        foreach (var jobStatus in projectJobsStatus)
                        {
                            Console.WriteLine($"job {jobStatus.JobId} module {jobStatus.Module} status {jobStatus.Status}");
                        }

                        Thread.Sleep(500);
                        //cfd
                        var isTerrainNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.Terrain) && x.Status != Status.Completed);
                        var isWindfieldsNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.Windfields) && x.Status != Status.Completed);
                        Console.WriteLine($"is isTerrainCompleted completed {!isTerrainNotCompleted} isWindfieldsCompleted {!isWindfieldsNotCompleted}");
                        if (!isTerrainNotCompleted && !isWindfieldsNotCompleted)
                        {
                            // todo: submit synthesis job
                            // first upload the synthesis input from source path
                            UploadSynthesisInput(new Guid(projectId), SourcePath).GetAwaiter().GetResult();
                            // then submit the job
                            SubmitSynthesisJob(new Guid(projectId)).GetAwaiter().GetResult();
                        }
                        Thread.Sleep(500);
                        //synthesis
                        var isObjectNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.Objects) && x.Status != Status.Completed);
                        var isWindResourcesNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.WindResources) && x.Status != Status.Completed);
                        Console.WriteLine($"is isObjectCompleted completed {!isObjectNotCompleted} isWindResourcesCompleted {!isWindResourcesNotCompleted}");
                        if (!isTerrainNotCompleted && !isWindfieldsNotCompleted && !isObjectNotCompleted && !isWindResourcesNotCompleted)
                        {
                            // todo: submit aep job
                            // first upload the aep input from source path
                            UploadAEPInput(new Guid(projectId), SourcePath).GetAwaiter().GetResult();
                            // then submit the job
                            SubmitAEPJob(new Guid(projectId)).GetAwaiter().GetResult();
                        }
                        Thread.Sleep(500);
                        //aep
                        var isLoadNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.Loads) && x.Status != Status.Completed);
                        var isEnergyNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.Energy) && x.Status != Status.Completed);
                        var isExportNotCompleted = projectJobsStatus.Any(x => (x.Module == Module.Exports) && x.Status != Status.Completed);
                        Console.WriteLine($"is AEP isLoadCompleted {!isLoadNotCompleted} isEnergyCompleted {!isEnergyNotCompleted} isExportCompleted {!isExportNotCompleted}");

                        if (!isTerrainNotCompleted && !isWindfieldsNotCompleted && !isLoadNotCompleted && !isEnergyNotCompleted && !isExportNotCompleted)
                        {
                            // todo: download the input to the destination path
                            DownloadProjectResult(new Guid(projectId), DestinationPath).GetAwaiter().GetResult();
                            Environment.Exit(0);
                        }

                    }

                });
                Console.WriteLine($"hubConnection {hubConnection.State} {hubConnection.ConnectionId}");
                Console.WriteLine("ConnectToJobNotificationHub: at the end of ConnectToJobNotificationHub");
                return hubConnection;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConnectToJobNotificationHub: {ex.Message}");
                throw ex;
            }

        }

        public async Task UploadCFDRansInput(Guid projectId, string srcPath)
        {
            Console.WriteLine("UploadProjectInput: at the start");
            try
            {
                var response = await _httpClient.GetAsync($"api/CFDRans/GetProjectInputUploadUri/{projectId}");
                response.EnsureSuccessStatusCode();
                var cfdInputUploadUri = await response.Content.ReadAsStringAsync();
                await _dataLoader.UploadInput(cfdInputUploadUri, srcPath);

                Console.WriteLine("UploadProjectInput: at the end");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UploadProjectInput: {ex.Message}");
                throw ex;
            }
        }

        public async Task UploadSynthesisInput(Guid projectId, string srcPath)
        {
            Console.WriteLine("UploadSynthesisInput: at the start");
            try
            {
                var response = await _httpClient.GetAsync($"api/Synthesis/GetSynthesisInputUploadUri/{projectId}");
                response.EnsureSuccessStatusCode();
                var synthesisInputUploadUri = await response.Content.ReadAsStringAsync();
                await _dataLoader.UploadInput(synthesisInputUploadUri, srcPath);

                Console.WriteLine("UploadSynthesisInput: at the end");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UploadSynthesisInput: {ex.Message}");
                throw ex;
            }
        }

        public async Task UploadAEPInput(Guid projectId, string srcPath)
        {
            Console.WriteLine("UploadAEPInput: at the start");
            try
            {
                var response = await _httpClient.GetAsync($"api/AEP/GetAEPInputUploadUri/{projectId}");
                response.EnsureSuccessStatusCode();
                var aepInputUploadUri = await response.Content.ReadAsStringAsync();
                await _dataLoader.UploadInput(aepInputUploadUri, srcPath);

                Console.WriteLine("UploadAEPInput: at the end");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UploadAEPInput: {ex.Message}");
                throw ex;
            }
        }

        public async Task<List<JobsStatusViewModel>> GetProjectStatus(string projectId)
        {
            Console.WriteLine("GetProjectStatus: at the start");
            try
            {
                var response = await _httpClient.GetFromJsonAsync<List<JobsStatusViewModel>>($"api/project/GetJobsStatus/{projectId}");

                Console.WriteLine("GetProjectStatus: at the end");
                return response;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetProjectStatus: {ex.Message}");
                throw ex;
            }

        }

        public async Task DownloadProjectResult(Guid projectId, string destinationPath)
        {
            Console.WriteLine("DownloadProjectResult: at the start");
            try
            {
                var response = await _httpClient.GetAsync($"api/Project/GetProjectOutputUri/{projectId}");
                response.EnsureSuccessStatusCode();
                var projectDownloadUri = await response.Content.ReadAsStringAsync();
                await _dataLoader.DownloadOutput(projectDownloadUri, destinationPath);

                Console.WriteLine("DownloadProjectResult: at the end");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DownloadProjectResult: {ex.Message}");
                throw ex;
            }
        }

        //end of class
    }
}
