using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    private static readonly Regex UserNameRegex =
        new(@"^[A-Za-z0-9\-._@+]{3,64}$", RegexOptions.Compiled);

    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
        {
            if (!IsValidRegistration(input))
                return Results.BadRequest(new { message = "Registration failed" });

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = input.UserName!.Trim(),
                Email = input.Email!.Trim(),
                DisplayName = input.DisplayName!.Trim()
            };

            var result = await users.CreateAsync(user, input.Password!);
            if (!result.Succeeded)
            {
                var codes = result.Errors.Select(e => e.Code).ToHashSet();
                return codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail")
                    ? Results.Conflict(new { message = "Account already exists" })
                    : Results.BadRequest(new { message = "Registration failed" });
            }

            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded)
                throw new InvalidOperationException("Default role assignment failed.");

            return Results.Created("/api/me",
                new RegisterResponse(user.Id, user.UserName!, user.DisplayName));
        });

        app.MapPost("/api/auth/login", async (
            LoginInput input,
            SignInManager<ApplicationUser> signIn,
            HttpContext httpContext,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SecureLab.Auth");

            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
            {
                logger.LogInformation("AuthEvent {EventType} {Result} {TraceId}",
                    "login", "failure", httpContext.TraceIdentifier);
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);

            logger.LogInformation("AuthEvent {EventType} {Result} {TraceId}",
                "login", result.Succeeded ? "success" : "failure", httpContext.TraceIdentifier);

            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/me", GetCurrentUser).RequireAuthorization();

        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private static async Task<IResult> GetCurrentUser(HttpContext httpContext, SecureLabDbContext db)
    {
        var idClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(idClaim, out var verifiedUserId))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

        var me = await db.Users.AsNoTracking()
            .Where(u => u.Id == verifiedUserId)
            .Select(u => new MeResponse(u.Id, u.UserName!, u.DisplayName))
            .SingleOrDefaultAsync();

        return me is null ? Results.Unauthorized() : Results.Ok(me);
    }

    private static bool IsValidRegistration(RegistrationInput r)
    {
        if (r.UserName is not { } userName || !UserNameRegex.IsMatch(userName.Trim()))
            return false;

        if (r.Email is not { } email)
            return false;
        var trimmedEmail = email.Trim();
        if (trimmedEmail.Length is 0 or > 256 || !new EmailAddressAttribute().IsValid(trimmedEmail))
            return false;

        if (r.DisplayName is not { } displayName
            || string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120)
            return false;

        return !string.IsNullOrEmpty(r.Password) && r.Password.Length <= 128;
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegisterResponse(Guid Id, string UserName, string DisplayName);
public sealed record MeResponse(Guid Id, string UserName, string DisplayName);