using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureLab.Api.Tests;

public sealed class AuthenticationMechanicsTests(
    SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Anonymous_Me_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RealCookie_ReachesSessionCheck()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var response = await client.GetAsync("/api/auth/session-check");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedMe_ReturnsCurrentUser()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "alice",
            json.RootElement.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task LoginCookie_HasRequiredSecurityFlags()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = Password
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var headers),
            "Login response must set an authentication cookie.");

        var cookieHeaders = headers!.ToArray();
        Assert.NotEmpty(cookieHeaders);

        var authCookie = cookieHeaders.FirstOrDefault(
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal));

        Assert.NotNull(authCookie);

        Assert.Contains("; httponly", authCookie!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", authCookie!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", authCookie!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FakeUserIdHeader_DoesNotChangeAuthenticatedUser()
    {
        using var client = await factory.LoginAsync("alice", Password);
        client.DefaultRequestHeaders.Add(
            "X-Demo-UserId",
            "10000000-0000-0000-0000-000000000002");

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "alice",
            json.RootElement.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task InvalidPassword_Returns401()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { userName = "alice", password = "DefinitelyWrongPassword123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesSessionCookie()
    {
        using var client = await factory.LoginAsync("alice", Password);

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
}
