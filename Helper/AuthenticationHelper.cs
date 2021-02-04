using Core.API.Common;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core.API.EndToEnd.Tests
{
    public class AuthenticationHelper
    {
        public static async Task<string> GetToken(HttpClient httpClient, IConfiguration configuration)
        {
            var token = string.Empty;

            var loginModel = new LoginModel() { Email = configuration["WindSim:UserName"], Password = configuration["WindSim:Password"] };
            var content = JsonSerializer.Serialize(loginModel);
            HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/Authentication/Login", httpContent);

            token = response.Content.ReadAsStringAsync().Result;

            return token;
        }
    }
}
