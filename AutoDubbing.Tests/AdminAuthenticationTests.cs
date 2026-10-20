using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class AdminAuthenticationTests
{
    [Fact]
    public async Task Login_and_me_return_profile_without_password_hash()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var id = await factory.SeedAdminAsync();
        using var login = await client.PostAsJsonAsync("/api/admin/auth/login",
            new { Username = "Admin", factory.Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);
        var response = await login.Content.ReadFromJsonAsync<AdminLoginResponse>();
        Assert.NotNull(response);
        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(id, response.Admin.Id);
        Assert.Equal("Active", response.Admin.Status);
        Assert.InRange(response.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(29),
            DateTimeOffset.UtcNow.AddMinutes(31));
        Assert.DoesNotContain("password", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        using var me = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
        Assert.Equal(response.Admin, await me.Content.ReadFromJsonAsync<AdminProfile>());
        Assert.DoesNotContain("password", await me.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("unknown-user")]
    [InlineData("locked")]
    [InlineData("malformed-hash")]
    public async Task Invalid_credentials_or_locked_account_return_same_401(string scenario)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        await factory.SeedAdminAsync(status: scenario == "locked" ? "Locked" : "Active",
            hashOverride: scenario == "malformed-hash" ? "invalid-base64!" : null);

        using var result = await client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Username = scenario == "unknown-user" ? "Unknown" : "Admin",
            Password = scenario == "wrong-password" ? Guid.NewGuid().ToString() : factory.Password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("Bearer", Assert.Single(result.Headers.WwwAuthenticate).Scheme);
        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Thông tin đăng nhập không hợp lệ hoặc tài khoản không hoạt động.", problem?.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    public async Task Missing_or_malformed_token_is_unauthorized(string? token)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Không cần tạo cả bảng: token không hợp lệ phải bị chặn trước khi truy vấn tài khoản.
        using var result = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("Bearer", Assert.Single(result.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unsigned")]
    [InlineData("algorithm")]
    [InlineData("missing-expiry")]
    [InlineData("missing-subject")]
    [InlineData("invalid-subject")]
    [InlineData("deleted-user")]
    public async Task Untrusted_tokens_are_rejected(string defect)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var id = await factory.SeedAdminAsync();
        var token = CreateToken(factory, id, defect);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var result = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Token_issued_before_account_is_locked_is_rejected_on_next_request()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var id = await factory.SeedAdminAsync();
        var token = await LoginAsync(client, "Admin", factory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var beforeLock = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.OK, beforeLock.StatusCode);

        await factory.ChangeStatusAsync(id, AdminAccountStatus.Locked);
        using var afterLock = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLock.StatusCode);
    }

    [Fact]
    public async Task Usernames_are_unique_and_case_sensitive()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var upperId = await factory.SeedAdminAsync("Admin");

        using var wrongCase = await client.PostAsJsonAsync("/api/admin/auth/login",
            new { Username = "admin", factory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCase.StatusCode);

        var lowerId = await factory.SeedAdminAsync("admin");
        Assert.Equal(upperId, (await LoginAsync(client, "Admin", factory.Password)).Admin.Id);
        Assert.Equal(lowerId, (await LoginAsync(client, "admin", factory.Password)).Admin.Id);
        await Assert.ThrowsAsync<DbUpdateException>(() => factory.SeedAdminAsync("Admin"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-long")]
    public async Task Invalid_login_payload_is_bad_request(string username)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        using var result = await client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Username = username == "too-long" ? new string('a', 101) : username,
            factory.Password
        });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Login_accepts_username_at_100_character_boundary()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var username = new string('A', 100);
        var id = await factory.SeedAdminAsync(username);
        Assert.Equal(id, (await LoginAsync(client, username, factory.Password)).Admin.Id);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("Unknown")]
    [InlineData("Active ")]
    public async Task Database_rejects_status_outside_active_and_locked(string status)
    {
        await using var factory = new AdminApiFactory();
        await Assert.ThrowsAsync<DbUpdateException>(() => factory.SeedAdminAsync(status: status));
    }

    [Fact]
    public async Task Successful_login_upgrades_legacy_identity_hash()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var legacyHasher = new PasswordHasher<AdminUser>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
        }));
        var legacyHash = legacyHasher.HashPassword(new AdminUser(), factory.Password);
        var id = await factory.SeedAdminAsync(hashOverride: legacyHash);
        await LoginAsync(client, "Admin", factory.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.SingleAsync(user => user.Id == id);
        Assert.NotEqual(legacyHash, admin.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>()
                .VerifyHashedPassword(admin, admin.PasswordHash, factory.Password));
    }

    [Fact]
    public async Task Local_provisioning_creates_only_first_admin_with_hashed_password()
    {
        await using var factory = new AdminApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAdminProvisioningService>();
        Assert.True(await service.CreateFirstAdminAsync("Admin", "Quản trị viên", factory.Password));
        Assert.False(await service.CreateFirstAdminAsync("Another", "Quản trị viên", factory.Password));

        var admin = await db.Users.SingleAsync();
        Assert.Equal("Active", admin.Status);
        Assert.NotEqual(factory.Password, admin.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>()
                .VerifyHashedPassword(admin, admin.PasswordHash, factory.Password));
    }

    [Fact]
    public async Task Application_start_does_not_seed_accounts_and_has_no_public_registration()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        Assert.Empty(await db.Users.ToListAsync());
        using var response = await client.PostAsJsonAsync("/api/admin/auth/register",
            new { Username = "Admin", factory.Password });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public async Task Startup_requires_a_valid_secret_signing_key(string? signingKey)
    {
        await using var factory = new AdminApiFactory(signingKey, omitSigningKey: signingKey is null);
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateHttpsClient());
        Assert.Contains("Jwt:SigningKey", exception.Message);
    }

    private static async Task<AdminLoginResponse> LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminLoginResponse>())!;
    }

    private static string CreateToken(AdminApiFactory factory, int id, string defect)
    {
        var now = DateTime.UtcNow;
        var subject = defect switch
        {
            "invalid-subject" => "not-an-id",
            "deleted-user" => (id + 1000).ToString(),
            _ => id.ToString()
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(defect == "missing-subject"
                ? [] : [new Claim("sub", subject)]),
            Issuer = defect == "issuer" ? "WrongIssuer" : "AutoDubbing.Tests",
            Audience = defect == "audience" ? "WrongAudience" : "AutoDubbing.Tests.Admin",
            NotBefore = defect == "future" ? now.AddHours(1) : now.AddMinutes(-10),
            IssuedAt = now.AddMinutes(-10),
            Expires = defect == "missing-expiry" ? null : defect == "expired" ? now.AddSeconds(-5) : now.AddHours(2),
            SigningCredentials = defect == "unsigned" ? null : new SigningCredentials(
                new SymmetricSecurityKey(defect == "signature"
                    ? RandomNumberGenerator.GetBytes(64) : Convert.FromBase64String(factory.SigningKey)),
                defect == "algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateEncodedJwt(descriptor);
    }
}
