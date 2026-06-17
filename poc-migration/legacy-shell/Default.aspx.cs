using System;
using System.Web;
using System.Web.Security;
using System.Web.UI;

namespace LegacyShell
{
    public partial class DefaultPage : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var jwt = Session["auth_jwt"] as string;
            if (string.IsNullOrEmpty(jwt))
            {
                FormsAuthentication.SignOut();
                Session.Clear();
                Response.Redirect("~/Login.aspx", endResponse: false);
                return;
            }

            lblUser.Text = HttpUtility.HtmlEncode(Session["username"] as string ?? "User");

            // Inject the JWT as a JS variable so the postMessage script can use it
            // without making another HTTP call. JavaScriptStringEncode prevents injection.
            var encoded = HttpUtility.JavaScriptStringEncode(jwt);
            LitJwtScript.Text = "<script>var __shellJwt = '" + encoded + "';</script>";
        }

        protected void BtnLogout_Click(object sender, EventArgs e)
        {
            FormsAuthentication.SignOut();
            Session.Clear();
            Response.Redirect("~/Login.aspx", endResponse: false);
        }
    }
}
