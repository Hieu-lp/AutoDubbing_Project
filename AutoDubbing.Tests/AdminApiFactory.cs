using System.Security.Cryptography;
using System.Text;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Storage;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Routes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AutoDubbing.Tests;

internal sealed class AdminApiFactory(string? signingKeyOverride = null, bool omitSigningKey = false)
    : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public string SigningKey { get; } = signingKeyOverride
        ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public string? RoutingApiKey { get; init; }
    public bool GenerationConfigured { get; init; }
    public ContentGenerationHttpStub GenerationHttp { get; } = new();
    public OrsHttpStub RoutingHttp { get; } = new();
    public CapturingLoggerProvider CapturedLogs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().AddProvider(CapturedLogs));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused;",
                ["Jwt:Issuer"] = "AutoDubbing.Tests",
                ["Jwt:Audience"] = "AutoDubbing.Tests.Admin",
                ["Jwt:LifetimeMinutes"] = "30",
                ["Jwt:SigningKey"] = omitSigningKey ? null : SigningKey,
                ["OpenRouteService:ApiKey"] = RoutingApiKey,
                ["OpenRouteService:TimeoutSeconds"] = "1",
                ["Gemini:ApiKey"] = GenerationConfigured ? "fixture-gemini-key" : null,
                ["Gemini:MaxRetries"] = "0",
                ["Supabase:ProjectUrl"] = GenerationConfigured ? "https://fixture.supabase.co" : null,
                ["Supabase:Bucket"] = GenerationConfigured ? "fixture-audio" : null,
                ["Supabase:UploadKey"] = GenerationConfigured ? "sb_secret_fixture_key" : null
            }));

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<IWalkingRouteProvider, OrsDirectionsProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => RoutingHttp);
            services.AddHttpClient<IContentProvider, GeminiContentProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => GenerationHttp);
            services.AddHttpClient<IAudioStorage, SupabaseAudioStorage>()
                .ConfigurePrimaryHttpMessageHandler(() => GenerationHttp);
            services.RemoveAll<IAudioCache>();
            services.AddSingleton<IAudioCache>(new MemoryAudioCache());
            services.RemoveAll<IGenerationDelay>();
            services.AddSingleton<IGenerationDelay>(new RecordingGenerationDelay());
            connection.Open();
            connection.CreateCollation("Latin1_General_100_BIN2", StringComparer.Ordinal.Compare);
            connection.CreateFunction<string, int>("DATALENGTH", value => Encoding.Unicode.GetByteCount(value));
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection)
                .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>());
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public async Task<int> SeedAdminAsync(string username = "Admin",
        string status = AdminAccountStatus.Active, string? hashOverride = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new InvalidOperationException("Test chỉ được ghi vào SQLite in-memory.");
        }

        await db.Database.EnsureCreatedAsync();
        var admin = new AdminUser { Username = username, FullName = "Admin kiểm thử", Status = status };
        admin.PasswordHash = hashOverride ?? scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>()
            .HashPassword(admin, Password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    public async Task ChangeStatusAsync(int id, string status)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(admin => admin.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(admin => admin.Status, status));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
    }
}

// Chỉ dùng trong test: SQLite không hiểu khai báo nvarchar(max) dành cho SQL Server.
internal sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<TourRoute>().Property(route => route.PoiIdsJson).HasColumnType("TEXT");
    }
}
