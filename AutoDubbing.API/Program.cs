using AutoDubbing.API.Authentication;
using AutoDubbing.API.Commands;
using AutoDubbing.API.Background;
using AutoDubbing.Service.ContentPreparation;
using AutoDubbing.Service.Monitoring;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using AutoDubbing.Data.Storage;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Authentication;
using AutoDubbing.Service.Pois;
using AutoDubbing.Service.Routes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Chưa cấu hình ConnectionStrings:DefaultConnection.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IAdminUserRepository, AdminUserRepository>();
builder.Services.AddScoped<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
builder.Services.AddScoped<IAdminProvisioningService, AdminProvisioningService>();

// Chỉ chạy khi người vận hành chủ động gọi lệnh; không seed khi khởi động API.
if (args is ["create-admin"])
{
    await using var commandApp = builder.Build();
    Environment.ExitCode = await CreateAdminCommand.RunAsync(commandApp.Services);
    return;
}

builder.Services.AddScoped<IAdminAuthenticationService, AdminAuthenticationService>();
builder.Services.AddScoped<AdminAccountService>();
builder.Services.AddAdminJwtAuthentication();
builder.Services.AddScoped<IPoiReadRepository, PoiReadRepository>();
builder.Services.AddScoped<IPoiReadService, PoiReadService>();
builder.Services.AddScoped<IPoiManagementService, PoiManagementService>();
builder.Services.AddScoped<IRoutePreviewService, RoutePreviewService>();
builder.Services.AddScoped<SavedRouteService>();
builder.Services.Configure<OrsDirectionsOptions>(builder.Configuration.GetSection(OrsDirectionsOptions.SectionName));
builder.Services.AddHttpClient<IWalkingRouteProvider, OrsDirectionsProvider>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();

builder.Services.AddScoped<IContentGenerationRepository, ContentGenerationRepository>();
builder.Services.AddScoped<IContentGenerationService, ContentGenerationService>();
builder.Services.AddSingleton<GenerationCoordinator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<VisitorPresence>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<SystemEventWriter>();
builder.Services.AddOptions<ContentPreparationOptions>()
    .Bind(builder.Configuration.GetSection(ContentPreparationOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.PlaybackRadiusMeters <= options.PreparationRadiusMeters,
        "PlaybackRadiusMeters must not exceed PreparationRadiusMeters.")
    .ValidateOnStart();
builder.Services.AddSingleton<IContentPreparationQueue, ContentPreparationQueue>();
builder.Services.AddSingleton<IPreparationRateGate, PreparationRateGate>();
builder.Services.AddScoped<IContentPreparationService, ContentPreparationService>();
builder.Services.AddHostedService<ContentPreparationWorker>();
builder.Services.AddSingleton<IGenerationDelay, GenerationDelay>();
builder.Services.AddSingleton<IAudioCache>(new LocalAudioCache(
    Path.Combine(builder.Environment.ContentRootPath, "artifacts", "audio-cache")));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.AddHttpClient<IContentProvider, GeminiContentProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddHttpClient<IAudioStorage, SupabaseAudioStorage>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Response lỗi không tiết lộ exception, stack trace hoặc cấu hình, kể cả trong Development.
app.UseExceptionHandler(handler => handler.Run(context => Results.Problem(
    statusCode: StatusCodes.Status500InternalServerError,
    title: "Không thể xử lý yêu cầu.").ExecuteAsync(context)));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Cho phép integration test khởi động đúng ứng dụng với database riêng.
public partial class Program;
