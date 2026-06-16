using System;

namespace LegacyShell
{
    public partial class DefaultPage : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // Stub authentication: in production this checks FormsAuthentication ticket.
            // For the spike, any visit to the page is treated as "Demo User" logged in.
            lblUser.Text = "Demo User";
        }
    }
}
