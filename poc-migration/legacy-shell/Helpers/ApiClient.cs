using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace LegacyShell.Helpers
{
    /// <summary>
    /// Centralized HTTP helper for all WebForms pages that need to call the API.
    ///
    /// Every call automatically attaches:
    ///   X-Api-Key: legacy-shell-v1   (application identifier)
    ///   Authorization: Bearer &lt;jwt&gt;  (when a token is in Session["auth_jwt"])
    ///
    /// Use the static Get/Post/Put/Delete helpers from any page code-behind.
    /// Never new-up HttpClient per call — socket exhaustion is a real problem on .NET Framework.
    /// </summary>
    public static class ApiClient
    {
        // Single shared HttpClient — prevents socket exhaustion on .NET Framework.
        private static readonly HttpClient _http = new HttpClient();

        private static readonly string _apiBaseUrl =
            (Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5050").TrimEnd('/');

        // Value distributed to the API as our client identity (not a secret).
        // Set LEGACY_SHELL_API_KEY in the environment to override without a code change.
        private static readonly string _appKey =
            Environment.GetEnvironmentVariable("LEGACY_SHELL_API_KEY") ?? "legacy-shell-v1";

        public static Task<HttpResponseMessage> GetAsync(string path)
            => SendAsync(HttpMethod.Get, path, null);

        public static Task<HttpResponseMessage> DeleteAsync(string path)
            => SendAsync(HttpMethod.Delete, path, null);

        public static Task<HttpResponseMessage> PostJsonAsync(string path, string json)
            => SendAsync(HttpMethod.Post, path, json);

        public static Task<HttpResponseMessage> PutJsonAsync(string path, string json)
            => SendAsync(HttpMethod.Put, path, json);

        private static Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string json)
        {
            var url = _apiBaseUrl + "/" + path.TrimStart('/');
            var req = new HttpRequestMessage(method, url);

            req.Headers.Add("X-Api-Key", _appKey);

            // Attach the JWT from Session when available.
            var jwt = HttpContext.Current?.Session?["auth_jwt"] as string;
            if (!string.IsNullOrEmpty(jwt))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            if (json != null)
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // Task.Run avoids the classic ASP.NET SynchronizationContext deadlock on .GetAwaiter().GetResult().
            return Task.Run(() => _http.SendAsync(req));
        }
    }
}
