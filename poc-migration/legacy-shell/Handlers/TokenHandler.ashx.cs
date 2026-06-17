using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

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
                using (var req = new HttpRequestMessage(HttpMethod.Post, apiBaseUrl + "/api/auth/token"))
                {
                    req.Headers.Add("X-Api-Key", shellApiKey);
                    // Classic ASP.NET has a SynchronizationContext that can deadlock on .Result;
                    // Task.Run moves the work to a thread-pool thread where no context is captured.
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

            // Store the JWT in server-side Session so code-behinds can call the API
            // without the browser JS needing to pass the token back server-side.
            // The browser still receives and uses the token for the postMessage→iframe flow.
            //
            // SECURITY NOTE (POC): The token is also returned as JSON to the browser below,
            // which stores it in JS memory and postMessages it to the Expo iframe. That path
            // has XSS exposure. For a production hardening pass: acquire the token
            // server-side at form-auth login, write to Session only, and never return the
            // raw JWT to the browser.
            if (statusCode == 200)
            {
                try
                {
                    var json = new JavaScriptSerializer()
                        .Deserialize<System.Collections.Generic.Dictionary<string, object>>(responseBody);

                    if (json.TryGetValue("access_token", out var tok) && tok is string jwt)
                        context.Session["auth_jwt"] = jwt;
                }
                catch
                {
                    // Non-fatal: Session write failure does not break the postMessage flow.
                }
            }

            context.Response.StatusCode = statusCode;
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.ContentType = "application/json";
            context.Response.Write(responseBody);
        }
    }
}
