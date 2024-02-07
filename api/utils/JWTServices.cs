using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using api.Models.Entity.NormalDB;
using Microsoft.IdentityModel.Tokens;

namespace api.utils
{
    public class JWTServices
    {
        private readonly IConfiguration config;
        private readonly SymmetricSecurityKey key;
        private readonly string issuer;
        private readonly string audience;
        public JWTServices(IConfiguration config)
        {
            this.config = config;
            string config_key = config["Jwt:Key"] ?? "";
            string config_issuer = config["Jwt:Issuer"] ?? "";
            string config_audience = config["Jwt:Audience"] ?? "";
            if (config_key == "" || config_issuer == "" || config_audience == "")
            {
                throw new Exception("JWT Key, Issuer, Audience is not found in appsettings.json");
            }
            this.issuer = config_issuer;
            this.audience = config_audience;
            key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config_key));
        }

        public string CreateToken(Users users, int expireDays = 30)
        {
            List<Claim> claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.NameId, users.userID.ToString()),
            };

            SigningCredentials credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            SecurityTokenDescriptor tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddDays(expireDays),
                SigningCredentials = credentials,
                Issuer = issuer,
                Audience = audience
            };

            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();

            SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }


    }
    public static class JWTServicesExtension
    {
        public static string? getUserID(this ClaimsPrincipal user)
        {
            return user.Claims.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        }
        
        public static string getAllCliams(this ClaimsPrincipal user)
        {
            string result = "";
            foreach (var claim in user.Claims)
            {
                result += $"{claim.Type}: {claim.Value}\n";
            }
            return result;
        }
    }
}