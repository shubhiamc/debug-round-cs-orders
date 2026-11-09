using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace OrderDesk;

public static class PasswordHasher
{
    private const int Iterations = 10_000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(Derive(password, salt)), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        var computed = Derive(password, Convert.FromBase64String(salt));
        return Convert.ToBase64String(computed) == hash;
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
}

public sealed class TokenClaims
{
    [JsonPropertyName("sub")] public int Sub { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("iat")] public long Iat { get; init; }
    [JsonPropertyName("exp")] public long Exp { get; init; }
}

/// <summary>
/// Issues and validates JWT-like bearer tokens: base64url(payload) + "." + base64url(HMAC-SHA256(payload)).
/// </summary>
public sealed class TokenService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider _time;

    public TokenService(TimeProvider time) => _time = time;

    public string Issue(User user)
    {
        var now = _time.GetUtcNow();
        var claims = new TokenClaims
        {
            Sub = user.Id,
            Name = user.Username,
            Iat = now.ToUnixTimeSeconds(),
            Exp = now.Add(Lifetime).ToUnixTimeSeconds()
        };
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims));
        return WebEncoders.Base64UrlEncode(payload) + "." + WebEncoders.Base64UrlEncode(Sign(payload));
    }

    public TokenClaims? Validate(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        try
        {
            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(Sign(payload), signature)) return null;

            var claims = JsonSerializer.Deserialize<TokenClaims>(payload);
            if (claims is null) return null;
            return claims;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private byte[] Sign(byte[] payload) => HMACSHA256.HashData(_key, payload);
}

public static class AuthHelper
{
    /// <summary>Returns the authenticated user, or null when the request has no valid bearer token.</summary>
    public static User? Authenticate(HttpContext ctx, TokenService tokens, Store store)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var claims = tokens.Validate(header.Substring(7).Trim());
        return claims is null ? null : store.GetUser(claims.Sub);
    }
}
