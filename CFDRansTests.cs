﻿using Core.API.Common;
using Core.API.Hubs.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        Task RunWindSimCoreJobs(string projectId);
    }

    public class CFDRansTests : ICFDRansTests
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        private bool IsCFDJobSubmitted { get; set; } = false;
        private bool IsSynthesisJobSubmitted { get; set; } = false;
        private bool IsAEPJobSubmitted { get; set; } = false;
        private DateTime startTime = DateTime.UtcNow;
        HubConnection hubConnection { get; set; }
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IDataLoader _dataLoader;
        private string accessToken;
        private readonly ILogger<ICFDRansTests> _logger;

        public CFDRansTests(HttpClient httpClient, IConfiguration configuration, IDataLoader dataLoader, ILogger<ICFDRansTests> logger)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromMinutes(20);
            accessToken = AuthenticationHelper.GetToken(_httpClient, configuration).GetAwaiter().GetResult();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _configuration = configuration;
            _dataLoader = dataLoader;
            _logger = logger;
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

                var response = await Policy
                    .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                     .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromMilliseconds(retryAttempt * 200), async (result, timeSpan, retryCount, context) =>
                     {
                     })
                     .ExecuteAsync(async () => await _httpClient.PostAsync("api/CFDrans/SubmitJob", httpContent));

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


                var response = await Policy
                    .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                     .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(retryAttempt * 200), async (result, timeSpan, retryCount, context) =>
                     {
                     })
                     .ExecuteAsync(async () => await _httpClient.PostAsync("api/Synthesis/SubmitJob", httpContent));

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


                var response = await Policy
                    .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                     .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(retryAttempt * 200), async (result, timeSpan, retryCount, context) =>
                     {
                     })
                     .ExecuteAsync(async () => await _httpClient.PostAsync("api/AEP/SubmitJob", httpContent));

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
                hubConnection = new HubConnectionBuilder()
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

                        Thread.Sleep(1500);
                        var anyPendingJob = projectJobsStatus.Any(x => x.Status != Status.Completed);
                        //cfd

                        string[] cfdModules = { "terrain", "windfields" };
                        projectJobsStatus = GetProjectStatus(projectId).GetAwaiter().GetResult();
                        var cfdModuleCount = projectJobsStatus.Where(q => cfdModules.Contains(q.Module.ToString().ToLower())).Select(q => q.Module).Distinct().Count();
                        Console.WriteLine($"cfdModuleCount {cfdModuleCount} anyPendingJob {anyPendingJob}");

                        if (!anyPendingJob && cfdModuleCount == 2 && !IsSynthesisJobSubmitted)
                        {
                            // todo: submit synthesis job
                            // first upload the synthesis input from source path
                            UploadSynthesisInput(new Guid(projectId), SourcePath).GetAwaiter().GetResult();
                            // then submit the job
                            SubmitSynthesisJob(new Guid(projectId)).GetAwaiter().GetResult();
                            IsSynthesisJobSubmitted = true;
                        }
                        Thread.Sleep(1500);
                        //synthesis

                        string[] synthesisModules = { "objects", "windresources" };
                        projectJobsStatus = GetProjectStatus(projectId).GetAwaiter().GetResult();
                        var synthesisModuleCount = projectJobsStatus.Where(q => synthesisModules.Contains(q.Module.ToString().ToLower())).Select(q => q.Module).Distinct().Count();
                        Console.WriteLine($"synthesisModuleCount {synthesisModuleCount} anyPendingJob {anyPendingJob}");


                        if (!anyPendingJob && synthesisModuleCount == 2 && !IsAEPJobSubmitted)
                        {

                            // todo: submit aep job
                            // first upload the aep input from source path
                            UploadAEPInput(new Guid(projectId), SourcePath).GetAwaiter().GetResult();
                            // then submit the job
                            SubmitAEPJob(new Guid(projectId)).GetAwaiter().GetResult();
                            IsAEPJobSubmitted = true;
                        }
                        Thread.Sleep(1500);
                        //aep

                        string[] aepModules = { "loads", "energy", "exports" };
                        projectJobsStatus = GetProjectStatus(projectId).GetAwaiter().GetResult();
                        var aepModuleCount = projectJobsStatus.Where(q => aepModules.Contains(q.Module.ToString().ToLower())).Select(q => q.Module).Distinct().Count();
                        Console.WriteLine($"aepModuleCount {aepModuleCount} anyPendingJob {anyPendingJob}");


                        if (!anyPendingJob && aepModuleCount == 3)
                        {
                            // todo: download the input to the destination path
                            DownloadProjectResult(new Guid(projectId), DestinationPath).GetAwaiter().GetResult();
                            Environment.Exit(0);
                        }

                        Console.WriteLine($"hubConnection {hubConnection.State} {hubConnection.ConnectionId} time {DateTime.Now.ToShortTimeString()}");
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

        public async Task RunWindSimCoreJobs(string projectId)
        {
            while ((DateTime.UtcNow - startTime) < TimeSpan.FromMinutes(280))
            {
                try
                {
                    //Console.WriteLine($"CheckStatusInInterval called at {DateTime.Now.ToShortTimeString()}");
                    await Task.Delay(5000);

                    var projectJobsStatus = GetProjectStatus(projectId).GetAwaiter().GetResult();

                    //foreach (var jobStatus in projectJobsStatus)
                    //{
                    //    Console.WriteLine($"job {jobStatus.JobId} module {jobStatus.Module} status {jobStatus.Status}");
                    //    _logger.LogInformation($"project {projectId} job IsSynthesisJobSubmitted {IsSynthesisJobSubmitted} {jobStatus.JobId} module {jobStatus.Module} status {jobStatus.Status}");
                    //}


                    var anyPendingJob = projectJobsStatus.Any(x => x.Status == Status.InProgress || x.Status == Status.Created);
                    //cfd

                    string[] cfdModules = { "terrain", "windfields" };
                    var cfdModuleCount = projectJobsStatus.Where(q => cfdModules.Contains(q.Module.ToString().ToLower())).Select(q => q.Module).Distinct().Count();
                    //Console.WriteLine($"cfdModuleCount {cfdModuleCount} anyPendingJob {anyPendingJob}");
                    //_logger.LogInformation($"{projectId} cfdModuleCount {cfdModuleCount} anyPendingJob {anyPendingJob}");

                    var isAnyWindfieldsJobCompleted = projectJobsStatus.Any(q => q.Status == Status.Completed && q.Module == Module.Windfields);

                    if (!anyPendingJob && cfdModuleCount == 2 && !IsSynthesisJobSubmitted && isAnyWindfieldsJobCompleted)
                    {
                        DownloadProjectResult(new Guid(projectId), DestinationPath).GetAwaiter().GetResult();
                        // first upload the synthesis input from source path
                        UploadSynthesisInput(new Guid(projectId), SourcePath).GetAwaiter().GetResult();
                        // then submit the job
                        SubmitSynthesisJob(new Guid(projectId)).GetAwaiter().GetResult();
                        IsSynthesisJobSubmitted = true;
                    }

                    //synthesis

                    string[] synthesisModules = { "objects", "windresources" };
                    var synthesisModuleCount = projectJobsStatus.Where(q => synthesisModules.Contains(q.Module.ToString().ToLower())).Select(q => q.Module).Distinct().Count();
                    //Console.WriteLine($"synthesisModuleCount {synthesisModuleCount} anyPendingJob {anyPendingJob}");
                    //_logger.LogInformation($"{projectId} synthesisModuleCount {synthesisModuleCount} anyPendingJob {anyPendingJob}");

                    if (!anyPendingJob && synthesisModuleCount == 2 && !IsAEPJobSubmitted)
                    {
                        DownloadProjectResult(new Guid(projectId), DestinationPath).GetAwaiter().GetResult();
                        // first upload the aep input from source path
                        UploadAEPInput(new Guid(projectId), SourcePath).GetAwaiter().GetResult();
                        // then submit the job
                        SubmitAEPJob(new Guid(projectId)).GetAwaiter().GetResult();
                        IsAEPJobSubmitted = true;
                    }

                    //aep

                    string[] aepModules = { "loads", "energy", "exports" };
                    var aepModuleCount = projectJobsStatus.Where(q => aepModules.Contains(q.Module.ToString().ToLower())).Select(q => q.Module).Distinct().Count();
                    //Console.WriteLine($"aepModuleCount {aepModuleCount} anyPendingJob {anyPendingJob}");
                    //_logger.LogInformation($"{projectId} aepModuleCount {aepModuleCount} anyPendingJob {anyPendingJob}");

                    if (!anyPendingJob && aepModuleCount > 0)
                    {
                        // todo: download the input to the destination path
                        DownloadProjectResult(new Guid(projectId), DestinationPath).GetAwaiter().GetResult();
                        //if energy is failed
                        var isAnyEnergyFailed = projectJobsStatus.Any(q => q.Module == Module.Energy && q.Status == Status.Failed);
                        var isAnyLoadFailed = projectJobsStatus.Any(q => q.Module == Module.Loads && q.Status == Status.Failed);
                        if (isAnyEnergyFailed || isAnyLoadFailed || aepModuleCount == 3)
                        {
                            // _logger.LogInformation("everythig completed successfully {proj}", projectId);
                            Environment.Exit(0);
                        }
                    }

                }
                catch (Exception ex)
                {
                    //    Console.WriteLine(ex.Message);
                    //    _logger.LogError(ex, " error {project} {message}", projectId, ex.Message);
                }

                //end of while loop
            }
            Environment.Exit(1);
        }

        //end of class
    }
}
