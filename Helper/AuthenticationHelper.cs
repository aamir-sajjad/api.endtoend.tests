using Core.API.Common;
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
        public static async Task<string> GetToken(HttpClient httpClient)
        {
            var token = string.Empty;
            //var registerModel = new RegisterModel() { Email = $"a{Guid.NewGuid().ToString().Substring(0, 4)}@a.com", Password = "Test@1234", OrganizationId = new Guid("d1276331-0a63-4bc9-a579-b7d40ed22040") };
            //var contentRegister = JsonSerializer.Serialize(registerModel);
            //HttpContent httpContentRegister = new StringContent(contentRegister, Encoding.UTF8, "application/json");
            //await httpClient.PostAsync("api/Authentication/Register", httpContentRegister);

            var loginModel = new LoginModel() { Email = "windsim@sewpg.com", Password = "sx@232kLw" };
            var content = JsonSerializer.Serialize(loginModel);
            HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/Authentication/Login", httpContent);

            token = response.Content.ReadAsStringAsync().Result;

            return token;
        }
    }
}
