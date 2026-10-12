using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApi.Api.Data;
using System.Security.Claims;
using System.Text;

namespace MyApi.Api.Auth
{
    public class JwtTokenService(string key, string issuer, string audience)
    {
        private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(key));

        public (string Token, DateTimeOffset ExpiresAtUtc) Create(User user)
        {
            var now = DateTimeOffset.UtcNow;
            var expires = now.AddHours(1);

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username)
                ]),
                Issuer = issuer,
                Audience = audience,
                NotBefore = now.UtcDateTime,
                Expires = expires.UtcDateTime,
                SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
            };

            return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
        }
    }

    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal principal) =>
            int.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    }
}
