using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace LegacyShell.Handlers
{
    public class TokenHandler : IHttpHandler
    {
        // Static so the underlying socket pool is reused across requests.
        private static readonly HttpClient _http = new HttpClient();

        public bool IsReusable => false;

        public void ProcessRequest(HttpContext context)
        {
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                return;
            }

            var apiBaseUrl  = Environment.GetEnvironmentVariable("API_BASE_URL")  ?? "http://localhost:5050";
            var shellApiKey = Environment.GetEnvironmentVariable("SHELL_API_KEY") ?? "poc-shell-api-key-change-in-production";

            string responseBody;
            int    statusCode;
            try
            {
                // Classic ASP.NET has a SynchronizationContext that can deadlock on .Result;
                // Task.Run moves the work to a thread-pool thread where no context is captured.
                using (var req = new HttpRequestMessage(HttpMethod.Post, apiBaseUrl + "/api/auth/token"))
                {
                    req.Headers.Add("X-Api-Key", shellApiKey);
                    var resp = Task.Run(() => _http.SendAsync(req)).GetAwaiter().GetResult();
                    responseBody = Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                    statusCode   = (int)resp.StatusCode;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode  = 503;
                context.Response.ContentType = "application/json";
                context.Response.Write("{\"error\":\"service_unavailable\",\"error_description\":\"" +
                                       ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
                return;
            }

            context.Response.StatusCode = statusCode;
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.ContentType = "application/json";
            context.Response.Write(responseBody);
        }
    }
}
