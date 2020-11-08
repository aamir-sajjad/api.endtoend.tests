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
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WindSim.Common.Model.Jobs;
using WindSim.Common.Model.Processing;

namespace Core.API.EndToEnd.Tests
{
    public interface ICFDRansTests
    {
        Task UploadProjectInput(Guid projectId);
        Task<string> SubmitJob(Guid projectId);
        Task<HubConnection> ConnectToJobNotificationHub(string accessToken, string projectId);
    }

    public class CFDRansTests : ICFDRansTests
    {
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

        public async Task<string> SubmitJob(Guid projectId)
        {
            try
            {
                Console.WriteLine("SubmitJob: at the start of SubmitJob");
                var timer = new Stopwatch();
                timer.Restart();
                timer.Start();

                var model = new CFDRansSubmitJobModel { ProjectId = projectId };
                var content = JsonSerializer.Serialize(model);
                HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("api/CFDrans/SubmitJob", httpContent);

                response.EnsureSuccessStatusCode();


                Console.WriteLine($"SubmitJob: at the end of SubmitJob took minutes: {timer.Elapsed.TotalMinutes}");
                return accessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitJob: {ex.Message}");
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
                        //cfd
                        var isCFDRan = projectJobsStatus.All(x => (x.Module == Module.Terrain || x.Module == Module.Windfields) && x.Status == Status.Completed);
                        Console.WriteLine($"isCFDRan {isCFDRan}");
                        if (isCFDRan)
                        {
                            // todo: submit synthesis job
                        }
                        //synthesis

                        //aep

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

        public async Task UploadProjectInput(Guid projectId)
        {
            Console.WriteLine("UploadProjectInput: at the start");
            try
            {
                var response = await _httpClient.GetAsync($"api/CFDRans/GetProjectInputUploadUri/{projectId}");
                response.EnsureSuccessStatusCode();
                var cfdInputUploadUri = await response.Content.ReadAsStringAsync();
                var srcPath = GetDefualtProjectInputPath();
                await _dataLoader.UploadInput(cfdInputUploadUri, srcPath);

                Console.WriteLine("UploadProjectInput: at the end");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UploadProjectInput: {ex.Message}");
                throw ex;
            }
        }

        private string GetDefualtProjectInputPath()
        {
            var fileName = "input.zip";
            var projectPath = Path.Combine("SEWPGProjectInput", fileName);
            return projectPath;
        }

        public async Task<List<JobsStatusViewModel>> GetProjectStatus(string projectId)
        {
            Console.WriteLine("GetProjectStatus: at the start");
            try
            {
                var response = await _httpClient.GetAsync($"api/project/GetJobsStatus/{projectId}");
                response.EnsureSuccessStatusCode();
                var statusMessageJson = await response.Content.ReadAsStringAsync();
                Console.WriteLine("GetProjectStatus: at the end");
                return JsonSerializer.Deserialize<List<JobsStatusViewModel>>(statusMessageJson);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetProjectStatus: {ex.Message}");
                throw ex;
            }

        }


        //end of class
    }
}
