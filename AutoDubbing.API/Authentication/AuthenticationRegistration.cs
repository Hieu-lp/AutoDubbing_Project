using System.Globalization;
using System.Security.Claims;
using AutoDubbing.Service.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace AutoDubbing.API.Authentication;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddAdminJwtAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Thiếu Jwt:Issuer.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Thiếu Jwt:Audience.")
            .Validate(options => options.HasValidSigningKey(),
                "Jwt:SigningKey phải là Base64 của ít nhất 32 byte; cấu hình bằng User Secrets hoặc biến môi trường.")
            .Validate(options => options.LifetimeMinutes is > 0 and <= 1440,
                "Jwt:LifetimeMinutes phải từ 1 đến 1440.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenIssuer, JwtTokenIssuer>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirstValue("sub");
                        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture,
                                out var adminId) || adminId <= 0)
                        {
                            context.Fail("Token không hợp lệ.");
                            return;
                        }

                        var service = context.HttpContext.RequestServices
                            .GetRequiredService<IAdminAuthenticationService>();
                        var admin = await service.GetActiveAdminAsync(adminId,
                            context.HttpContext.RequestAborted);
                        if (admin is null)
                        {
                            // Kiểm tra DB mỗi request: token đã cấp cũng bị từ chối khi tài khoản bị khóa/xóa.
                            context.Fail("Tài khoản không hoạt động.");
                        }
                    }
                };
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
                bearer.TokenValidationParameters = jwt.Value.CreateValidationParameters());
        services.AddAuthorization();
        return services;
    }
}
