using System;
using System.Collections.Generic;
using System.Net;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.UI;
using LegacyShell.Helpers;

namespace LegacyShell
{
    public partial class DefaultPage : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var jwt      = Session["auth_jwt"] as string;
            var username = Session["username"]  as string;

            if (string.IsNullOrEmpty(jwt))
            {
                PnlLogin.Visible = true;
                PnlShell.Visible = false;
                return;
            }

            // Logged in — show the shell and inject the JWT for postMessage.
            PnlLogin.Visible = false;
            PnlShell.Visible = true;
            lblUser.Text     = HttpUtility.HtmlEncode(username ?? "User");

            // Emit a small script block so the in-page JS can postMessage the token
            // to the Expo iframe without making another HTTP call.
            var encoded = HttpUtility.JavaScriptStringEncode(jwt);
            LitJwtScript.Text = "<script>var __shellJwt = '" + encoded + "';</script>";
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
                body = System.Threading.Tasks.Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
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
                    Response.Redirect("~/Default.aspx", endResponse: false);
                    Context.ApplicationInstance.CompleteRequest();
                }
                else
                {
                    ShowError("Unexpected response from login service.");
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

        protected void BtnLogout_Click(object sender, EventArgs e)
        {
            Session.Clear();
            Response.Redirect("~/Default.aspx", endResponse: false);
            Context.ApplicationInstance.CompleteRequest();
        }

        private void ShowError(string message)
        {
            LblError.Text    = HttpUtility.HtmlEncode(message);
            LblError.Visible = true;
            PnlLogin.Visible = true;
            PnlShell.Visible = false;
        }
    }
}
