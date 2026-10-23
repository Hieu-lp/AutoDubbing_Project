namespace AutoDubbing.Service.Authentication;

public sealed record IssuedAccessToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AdminProfile admin);
}
