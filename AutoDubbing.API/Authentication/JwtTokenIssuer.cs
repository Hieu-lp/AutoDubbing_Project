using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AutoDubbing.Service.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AutoDubbing.API.Authentication;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
    : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(AdminProfile admin)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.UniqueName, admin.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(),
                SecurityAlgorithms.HmacSha256)
        };

        return new IssuedAccessToken(new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor), expires);
    }
}
