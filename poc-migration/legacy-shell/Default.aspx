<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="LegacyShell.DefaultPage" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Legacy WebForms Shell</title>
  <style>
    body { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
    header { background: #1a3c5e; color: #fff; padding: 12px 16px; border-radius: 4px; margin-bottom: 12px; }
    iframe { width: 100%; height: 600px; border: 2px solid #1a3c5e; border-radius: 4px; background: #fff; }
    #status { font-size: 0.85rem; color: #555; margin-bottom: 8px; }
  </style>
</head>
<body>
  <form id="shellForm" runat="server">
    <header>
      <strong>Legacy WebForms Shell</strong> &mdash; Logged in as
      <em><asp:Label ID="lblUser" runat="server" /></em>
      &nbsp;(Real ASP.NET WebForms on .NET Framework 4.8)
    </header>
    <div id="status">Fetching JWT from token endpoint&hellip;</div>
    <!-- src is set by JS only after the token is obtained so the auth gate sees it
         on the very first request to the Expo entry point. -->
    <iframe id="expo-frame" style="display:none;width:100%;height:600px;border:2px solid #1a3c5e;border-radius:4px;background:#fff;"></iframe>
  </form>

  <script>
    fetch('/Handlers/TokenHandler.ashx', { method: 'POST' })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        document.getElementById('status').textContent = 'JWT obtained. Loading Expo app…';
        var frame = document.getElementById('expo-frame');
        frame.src = 'http://localhost:8081?token=' + encodeURIComponent(data.access_token);
        frame.style.display = '';
      })
      .catch(function(err) {
        document.getElementById('status').textContent = 'Token fetch failed: ' + err;
      });

    document.getElementById('expo-frame').addEventListener('load', function() {
      document.getElementById('status').textContent = 'Expo app loaded ✓';
    });
  </script>
</body>
</html>
