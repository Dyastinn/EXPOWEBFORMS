<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Login.aspx.cs" Inherits="LegacyShell.LoginPage" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Sign in — Legacy WebForms Shell</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; }
    body {
      font-family: sans-serif; margin: 0; background: #f4f4f4;
      display: flex; justify-content: center; align-items: center;
      min-height: 100vh;
    }
    .login-card {
      background: #fff; border-radius: 10px; padding: 36px 32px;
      width: 100%; max-width: 380px;
      border: 1px solid #dde1e7;
      box-shadow: 0 4px 14px rgba(0,0,0,0.08);
    }
    .login-title    { font-size: 20px; font-weight: 700; color: #1a3c5e; margin: 0 0 4px; }
    .login-subtitle { font-size: 13px; color: #888; margin: 0 0 26px; }
    .field          { margin-bottom: 14px; }
    label           { display: block; font-size: 13px; font-weight: 600; color: #444; margin-bottom: 4px; }
    .text-input {
      width: 100%; padding: 9px 10px; font-size: 14px;
      border: 1px solid #ccc; border-radius: 4px;
      background: #fafafa; outline: none;
    }
    .text-input:focus { border-color: #1a3c5e; box-shadow: 0 0 0 2px rgba(26,60,94,0.12); }
    .btn-primary {
      width: 100%; padding: 11px; font-size: 14px; font-weight: 700;
      background: #1a3c5e; color: #fff;
      border: none; border-radius: 4px; cursor: pointer; margin-top: 6px;
    }
    .btn-primary:hover  { background: #15304e; }
    .btn-primary:active { background: #0f2236; }
    .login-error {
      display: block; background: #fdecea; color: #c0392b;
      border: 1px solid #f5c6c6; border-radius: 4px;
      padding: 9px 12px; font-size: 13px; margin-bottom: 16px;
    }
  </style>
</head>
<body>
  <form id="loginForm" runat="server">
    <div class="login-card">
      <p class="login-title">Legacy WebForms Shell</p>
      <p class="login-subtitle">Sign in to continue &mdash; ASP.NET 4.8</p>

      <asp:Label ID="LblError" runat="server" CssClass="login-error" Visible="false" />

      <div class="field">
        <label for="<%= TxtUsername.ClientID %>">Username</label>
        <asp:TextBox ID="TxtUsername" runat="server" CssClass="text-input" AutoCompleteType="Disabled" />
      </div>
      <div class="field">
        <label for="<%= TxtPassword.ClientID %>">Password</label>
        <asp:TextBox ID="TxtPassword" runat="server" TextMode="Password" CssClass="text-input" />
      </div>

      <asp:Button ID="BtnLogin" runat="server" Text="Sign in" CssClass="btn-primary" OnClick="BtnLogin_Click" />
    </div>
  </form>
</body>
</html>
