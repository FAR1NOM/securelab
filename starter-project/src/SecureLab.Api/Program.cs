using SecureLab.Api.Scaffolding;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Client"
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("SecureLab")
    ?? throw new InvalidOperationException(
        "Connection string 'SecureLab' is not configured.");

builder.Services.AddDbContext<SecureLabDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IncidentQueries>();

builder.AddLab03Identity();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue(
                    "RateLimit:Login:PermitLimit", 100),
                Window = TimeSpan.FromSeconds(
                    builder.Configuration.GetValue(
                        "RateLimit:Login:WindowSeconds", 60)),
                QueueLimit = 0
            }));

    options.AddPolicy("incident-search", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue(
                    "RateLimit:Search:PermitLimit", 30),
                Window = TimeSpan.FromSeconds(
                    builder.Configuration.GetValue(
                        "RateLimit:Search:WindowSeconds", 60)),
                QueueLimit = 0
            }));

    options.AddPolicy("incident-create", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue(
                    "RateLimit:Create:PermitLimit", 5),
                Window = TimeSpan.FromSeconds(
                    builder.Configuration.GetValue(
                        "RateLimit:Create:WindowSeconds", 60)),
                QueueLimit = 0
            }));
});

var app = builder.Build();

var resetRequested = args.Contains("--reset-database", StringComparer.Ordinal);

await DatabaseBootstrap.InitializeAsync(
    app.Services,
    app.Configuration,
    app.Environment,
    resetRequested);

if (resetRequested)
{
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (
    SecureLabDbContext dbContext,
    CancellationToken cancellationToken) =>
        await dbContext.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready" })
            : Results.Problem(
                title: "Database unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetHealth")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapIncidentEndpoints();
app.MapLab03Identity();
app.MapLab02Endpoints();
app.MapFallbackToFile("index.html");

app.Run();

static string GetRateLimitKey(HttpContext context)
{
    return context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

public partial class Program;