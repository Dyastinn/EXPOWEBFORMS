using System;
using System.Collections.Generic;
using System.Net;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Security;
using System.Web.UI;
using LegacyShell.Helpers;

namespace LegacyShell
{
    public partial class LoginPage : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // Guard against stale auth cookie with no matching session JWT (would cause a loop).
            if (!IsPostBack && Request.IsAuthenticated && Session["auth_jwt"] != null)
            {
                Response.Redirect("~/Default.aspx", endResponse: false);
            }
        }

        protected void BtnLogin_Click(object sender, EventArgs e)
        {
            var username = TxtUsername.Text.Trim();
            var password = TxtPassword.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Username and password are required.");
                return;
            }

            var payload = new JavaScriptSerializer().Serialize(new { username, password });

            System.Net.Http.HttpResponseMessage resp;
            string body;
            try
            {
                resp = ApiClient.PostJsonAsync("/api/auth/login", payload).GetAwaiter().GetResult();
                body = System.Threading.Tasks.Task
                    .Run(() => resp.Content.ReadAsStringAsync())
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ShowError("Could not reach the API: " + ex.Message);
                return;
            }

            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var data = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(body);

                if (data.TryGetValue("access_token", out var tok) && tok is string jwt)
                {
                    Session["auth_jwt"] = jwt;
                    Session["username"] = username;
                    FormsAuthentication.SetAuthCookie(username, false);
                    var returnUrl = FormsAuthentication.GetRedirectUrl(username, false);
                    Response.Redirect(returnUrl, endResponse: false);
                }
                else
                {
                    ShowError("Unexpected response from the login service.");
                }
            }
            else if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                ShowError("Invalid username or password.");
            }
            else if ((int)resp.StatusCode == 429)
            {
                ShowError("Too many login attempts. Please wait a moment and try again.");
            }
            else
            {
                ShowError("Login failed (HTTP " + (int)resp.StatusCode + ").");
            }
        }

        private void ShowError(string message)
        {
            LblError.Text    = HttpUtility.HtmlEncode(message);
            LblError.Visible = true;
        }
    }
}
