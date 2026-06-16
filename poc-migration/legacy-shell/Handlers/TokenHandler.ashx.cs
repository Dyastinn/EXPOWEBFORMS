using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Web;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace LegacyShell.Handlers
{
    public class TokenHandler : IHttpHandler
    {
        public bool IsReusable => false;

        public void ProcessRequest(HttpContext context)
        {
            // Only respond to POST (matching the shell-sim's /token endpoint contract)
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                return;
            }

            var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
            if (string.IsNullOrEmpty(jwtSecret))
            {
                context.Response.StatusCode = 500;
                context.Response.Write("JWT_SECRET not set");
                return;
            }

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var now   = DateTime.UtcNow;

            var token = new JwtSecurityToken(
                issuer:             "poc-legacy-shell",
                audience:           "poc-api",
                claims:             new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Sub,  "demo-user"),
                    new Claim(JwtRegisteredClaimNames.Name, "Demo User"),
                    new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString())
                },
                notBefore:          now,
                expires:            now.AddHours(1),
                signingCredentials: creds);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            // Allow cross-origin requests so the browser can call this from the shell page
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.ContentType = "application/json";
            context.Response.Write(JsonConvert.SerializeObject(new
            {
                access_token = tokenString,
                token_type   = "Bearer",
                expires_in   = 3600
            }));
        }
    }
}
