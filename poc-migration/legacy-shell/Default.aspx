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
    <div id="status">Fetching JWT…</div>
    <iframe id="expo-frame" src="http://localhost:8081" style="width:100%;height:600px;border:2px solid #1a3c5e;border-radius:4px;background:#fff;"></iframe>
  </form>

  <script>
    var frame        = document.getElementById('expo-frame');
    var targetOrigin = 'http://localhost:8081';
    var pendingToken = null;
    var frameReady   = false;

    function trySendToken() {
      if (pendingToken && frameReady) {
        frame.contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, targetOrigin);
        document.getElementById('status').textContent = 'Expo app loaded ✓';
      }
    }

    frame.addEventListener('load', function() {
      frameReady = true;
      document.getElementById('status').textContent = pendingToken
        ? 'Expo loaded. Sending token…'
        : 'Expo loaded. Waiting for token…';
      trySendToken();
    });

    fetch('/Handlers/TokenHandler.ashx', { method: 'POST' })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        pendingToken = data.access_token;
        document.getElementById('status').textContent = frameReady
          ? 'Token obtained. Sending…'
          : 'Token obtained. Waiting for Expo to load…';
        trySendToken();
      })
      .catch(function(err) {
        document.getElementById('status').textContent = 'Token fetch failed: ' + err;
      });
  </script>
</body>
</html>
