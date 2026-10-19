using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel;

// Minimal JWT minter for dev/test logins. Production uses the same secret/claims
// shape; password verification + refresh/rotation are wired by ops config.
public static class JwtMint
{
    public static string Mint(string secret, IDictionary<string, string> claims, TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var token = new JwtSecurityToken(
            claims: claims.Select(kv => new Claim(kv.Key, kv.Value)),
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(8)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Validated (and dev-defaulted) at startup by RequireSecret; no fallback here.
    public static string Secret(IConfiguration cfg) =>
        cfg["Jwt:SecretKey"] ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");
}
