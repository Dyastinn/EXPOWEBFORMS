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
    <iframe id="expo-frame" src="http://localhost:8081"></iframe>
  </form>

  <script>
    const EXPO_ORIGIN = 'http://localhost:8081';
    let pendingToken = null;
    let frameReady   = false;

    fetch('/Handlers/TokenHandler.ashx', { method: 'POST' })
      .then(r => r.json())
      .then(({ access_token }) => {
        pendingToken = access_token;
        document.getElementById('status').textContent = 'JWT obtained. Waiting for Expo frame…';
        trySend();
      })
      .catch(err => {
        document.getElementById('status').textContent = 'Token fetch failed: ' + err;
      });

    window.addEventListener('message', function(event) {
      if (event.origin !== EXPO_ORIGIN) return;
      if (event.data?.type === 'EXPO_READY') {
        frameReady = true;
        document.getElementById('status').textContent = 'Expo frame ready. Sending JWT…';
        trySend();
      }
    });

    function trySend() {
      if (!pendingToken || !frameReady) return;
      document.getElementById('expo-frame')
        .contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, EXPO_ORIGIN);
      document.getElementById('status').textContent = 'JWT handed to Expo frame via postMessage ✓';
    }

    document.getElementById('expo-frame').addEventListener('load', function() {
      frameReady = true;
      trySend();
    });
  </script>
</body>
</html>
