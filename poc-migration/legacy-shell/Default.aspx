<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="LegacyShell.DefaultPage" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Legacy WebForms Shell</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; }
    body   { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
    header {
      background: #1a3c5e; color: #fff; padding: 12px 16px;
      border-radius: 4px; margin-bottom: 10px;
      display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
    }
    .logout-btn {
      margin-left: auto; font-size: 12px; color: #a8c4e0;
      text-decoration: underline; cursor: pointer;
      background: none; border: none; padding: 0;
    }
    .logout-btn:hover { color: #fff; }
    #status { font-size: 0.85rem; color: #555; margin-bottom: 8px; }
    #layout { display: flex; gap: 12px; align-items: flex-start; }
    #expo-frame { flex: 1; min-width: 0; height: 600px; border: 2px solid #1a3c5e; border-radius: 4px; background: #fff; }

    /* Debug panel */
    #dbg { width: 380px; flex-shrink: 0; background: #1e1e2e; border: 1px solid #3a3a5c;
           border-radius: 6px; font-family: monospace; font-size: 12px; overflow: hidden; }
    #dbg-header { background: #2a2a40; color: #7c8cf8; padding: 8px 12px; cursor: pointer;
                  display: flex; justify-content: space-between; align-items: center; font-weight: 700;
                  font-size: 11px; letter-spacing: 0.8px; text-transform: uppercase; user-select: none; }
    #dbg-body { padding: 12px; max-height: 580px; overflow-y: auto; }
    .dbg-section { color: #7c8cf8; font-weight: 700; font-size: 10px; text-transform: uppercase;
                   letter-spacing: 0.8px; margin: 10px 0 4px; }
    .dbg-section:first-child { margin-top: 0; }
    .dbg-row { display: flex; gap: 8px; border-bottom: 1px solid #2a2a40; padding: 3px 0; }
    .dbg-key { color: #9cdcfe; width: 120px; flex-shrink: 0; }
    .dbg-val { color: #ce9178; flex: 1; word-break: break-all; }
    .dbg-jwt { color: #4ec9b0; background: #252540; padding: 6px; border-radius: 4px;
               word-break: break-all; font-size: 10px; margin: 4px 0; }
    .dbg-pre { background: #252540; color: #b5cea8; padding: 8px; border-radius: 4px;
               white-space: pre; overflow-x: auto; font-size: 11px; margin: 4px 0; }
    .log-entry { display: flex; gap: 8px; border-bottom: 1px solid #2a2a40; padding: 3px 0; }
    .log-ts    { color: #5a5a7a; width: 86px; flex-shrink: 0; font-size: 10px; }
    .log-msg   { flex: 1; }
    .log-detail { color: #6a9955; font-size: 10px; }
    #dbg-log { max-height: 180px; overflow-y: auto; }
  </style>
</head>
<body>
  <form id="shellForm" runat="server">
    <header>
      <strong>Legacy WebForms Shell</strong> &mdash; Logged in as
      <em><asp:Label ID="lblUser" runat="server" /></em>
      &nbsp;(ASP.NET WebForms .NET 4.8)
      <asp:LinkButton ID="BtnLogout" runat="server" CssClass="logout-btn" OnClick="BtnLogout_Click">Sign out</asp:LinkButton>
    </header>
    <div id="status">Loading…</div>

    <div id="layout">
      <iframe id="expo-frame" src="http://localhost:8081"></iframe>

      <div id="dbg">
        <div id="dbg-header" onclick="document.getElementById('dbg-body').style.display=document.getElementById('dbg-body').style.display==='none'?'block':'none'">
          <span>Shell Debug</span><span>▲</span>
        </div>
        <div id="dbg-body">
          <div class="dbg-section">Identity</div>
          <div class="dbg-row"><span class="dbg-key">Shell origin</span><span class="dbg-val" id="d-origin"></span></div>
          <div class="dbg-row"><span class="dbg-key">Framework</span><span class="dbg-val">ASP.NET WebForms .NET 4.8</span></div>
          <div class="dbg-row"><span class="dbg-key">iframe src</span><span class="dbg-val">http://localhost:8081</span></div>
          <div class="dbg-row"><span class="dbg-key">postMessage target</span><span class="dbg-val">http://localhost:8081</span></div>

          <div class="dbg-section">Token Flow</div>
          <div class="dbg-row"><span class="dbg-key">Auth method</span><span class="dbg-val">POST /api/auth/login (Login.aspx, server-side)</span></div>
          <div class="dbg-row"><span class="dbg-key">Token storage</span><span class="dbg-val">ASP.NET Session["auth_jwt"]</span></div>
          <div class="dbg-row"><span class="dbg-key">JWT_SECRET held</span><span class="dbg-val" style="color:#e57373">NO — API only</span></div>

          <div class="dbg-section">JWT Received</div>
          <div class="dbg-jwt" id="d-jwt">(loading…)</div>

          <div class="dbg-section">Decoded Payload</div>
          <div class="dbg-pre" id="d-payload">(loading…)</div>

          <div class="dbg-section">postMessage Sent</div>
          <div class="dbg-row"><span class="dbg-key">Sent at</span><span class="dbg-val" id="d-pm-time">—</span></div>
          <div class="dbg-row"><span class="dbg-key">targetOrigin</span><span class="dbg-val">http://localhost:8081</span></div>
          <div class="dbg-row"><span class="dbg-key">Payload type</span><span class="dbg-val" id="d-pm-type">—</span></div>

          <div class="dbg-section">Event Log</div>
          <div id="dbg-log"></div>
        </div>
      </div>
    </div>

    <%-- JWT injected from Session by code-behind — consumed by postMessage JS below --%>
    <asp:Literal ID="LitJwtScript" runat="server" />

    <script>
      var frame        = document.getElementById('expo-frame');
      var targetOrigin = 'http://localhost:8081';
      var pendingToken = null;
      var frameReady   = false;

      // ── Debug helpers ─────────────────────────────────────────────────────
      document.getElementById('d-origin').textContent = window.location.origin;

      function dbgLog(msg, detail) {
        var ts  = new Date().toISOString().slice(11, 23);
        var row = document.createElement('div');
        row.className = 'log-entry';
        row.innerHTML = '<span class="log-ts">' + ts + '</span>'
          + '<span class="log-msg">' + msg
          + (detail ? '<div class="log-detail">' + detail + '</div>' : '')
          + '</span>';
        var log = document.getElementById('dbg-log');
        log.appendChild(row);
        log.scrollTop = log.scrollHeight;
      }

      function dbgSetJwt(token) {
        document.getElementById('d-jwt').textContent = token;
        try {
          var parts   = token.split('.');
          var payload = JSON.parse(atob(parts[1].replace(/-/g,'+').replace(/_/g,'/')));
          if (typeof payload.exp === 'number') {
            var expDate  = new Date(payload.exp * 1000);
            var secsLeft = Math.round((expDate - Date.now()) / 1000);
            payload._exp_readable = expDate.toLocaleTimeString()
              + ' (' + Math.floor(secsLeft/60) + 'm ' + (secsLeft%60) + 's remaining)';
          }
          if (typeof payload.iat === 'number')
            payload._iat_readable = new Date(payload.iat * 1000).toLocaleTimeString();
          document.getElementById('d-payload').textContent = JSON.stringify(payload, null, 2);
        } catch(e) {
          document.getElementById('d-payload').textContent = 'decode error: ' + e;
        }
      }

      // ── postMessage flow ──────────────────────────────────────────────────
      function trySendToken() {
        if (pendingToken && frameReady) {
          frame.contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, targetOrigin);
          var sentAt = new Date().toISOString().slice(11, 23);
          document.getElementById('d-pm-time').textContent = sentAt;
          document.getElementById('d-pm-type').textContent = 'AUTH_TOKEN';
          dbgLog('postMessage sent', 'type=AUTH_TOKEN  target=' + targetOrigin);
          document.getElementById('status').textContent = 'Token sent via postMessage ✓';
        }
      }

      frame.addEventListener('load', function() {
        frameReady = true;
        dbgLog('iframe load event fired', 'src=http://localhost:8081');
        document.getElementById('status').textContent = pendingToken
          ? 'Expo loaded. Sending token…'
          : 'Expo loaded. Waiting for token…';
        trySendToken();
      });

      // JWT was issued server-side on Login.aspx and stored in ASP.NET Session.
      // Code-behind injects it as __shellJwt above — no client-side API call needed.
      if (typeof __shellJwt !== 'undefined' && __shellJwt) {
        pendingToken = __shellJwt;
        dbgSetJwt(__shellJwt);
        dbgLog('JWT loaded from Session', 'issued via POST /api/auth/login');
        document.getElementById('status').textContent = frameReady
          ? 'Token obtained. Sending…'
          : 'Token obtained. Waiting for Expo to load…';
        trySendToken();
      } else {
        dbgLog('No JWT in Session — redirecting to login');
        document.getElementById('status').textContent = 'No token — redirecting…';
        window.location.href = '/Login.aspx';
      }
    </script>
  </form>
</body>
</html>
