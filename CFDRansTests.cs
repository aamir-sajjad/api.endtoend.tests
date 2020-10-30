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

                    Console.WriteLine($" message received module {message.Module} job {message.JobId} status {message.Status}");

                    Console.WriteLine($"First message received after minutes : {timer.Elapsed.TotalMinutes}");

                    ListOutputFiles(projectId, message.JobId, message.Module).GetAwaiter().GetResult();

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

        private async Task ListOutputFiles(string projectId, string jobId, Module module)
        {
            Console.WriteLine($"ListOutputFiles: at the start of ListOutputFiles project {projectId} {jobId} {module}");
            try
            {
                var modelJobOutput = new JobOutputViewModel()
                {
                    ProjectId = new Guid(projectId),
                    JobId = new Guid(jobId),
                    Module = module
                };
                var content = JsonSerializer.Serialize(modelJobOutput);
                Console.WriteLine($"modelJobOutput {content}");
                HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/CFDrans/ListOutputFiles", httpContent);


                response.EnsureSuccessStatusCode();

                var jsonJobObject = await response.Content.ReadAsStringAsync();
                var files = JsonSerializer.Deserialize<List<string>>(jsonJobObject);
                foreach (var fileName in files)
                {
                    Console.WriteLine(fileName);
                }
                Console.WriteLine("ListOutputFiles: at the end of ListOutputFiles");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ListOutputFiles: {ex.Message}");
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

        //end of class
    }
}
