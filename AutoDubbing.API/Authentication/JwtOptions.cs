using Microsoft.IdentityModel.Tokens;

namespace AutoDubbing.API.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    // Base64; lấy từ User Secrets hoặc biến môi trường, không đưa vào appsettings.
    public string SigningKey { get; init; } = string.Empty;
    public int LifetimeMinutes { get; init; } = 30;

    public bool HasValidSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(SigningKey).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public SymmetricSecurityKey CreateSigningKey() => new(Convert.FromBase64String(SigningKey));

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(),
        RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        RequireAudience = true,
        ValidAudience = Audience,
        IgnoreTrailingSlashWhenValidatingAudience = false,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        ValidTypes = ["JWT"],
        NameClaimType = "unique_name"
    };
}
