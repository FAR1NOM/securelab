using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class RegisterTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static string NewName() => "reg" + Guid.NewGuid().ToString("N")[..8];

    private static object Body(string name, string? email = null, string? password = null,
        string displayName = "Студент 01") =>
        new { userName = name, email = email ?? name + "@example.test",
              displayName, password = password ?? Password };

    [Fact]
    public async Task T01_Register_Valid_Returns201_WithoutSensitiveFields()
    {
        using var client = factory.CreateClient();
        var name = NewName();
        using var response = await client.PostAsJsonAsync("/api/auth/register", Body(name));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(Guid.TryParse(root.GetProperty("id").GetString(), out _));
        Assert.Equal(name, root.GetProperty("userName").GetString());
        foreach (var forbidden in new[] { "password", "confirmPassword", "passwordHash", "securityStamp", "token", "cookie" })
            Assert.False(root.TryGetProperty(forbidden, out _), $"Field leaked: {forbidden}");
    }

    [Fact]
    public async Task T02_Register_WeakPassword_Returns400_SafeBody()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            Body(NewName(), password: "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("short", text);
    }

    [Fact]
    public async Task Register_TooLongDisplayName_Returns400()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            Body(NewName(), displayName: new string('x', 121)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_Duplicate_Returns409_SameBodyForUserNameAndEmail()
    {
        using var client = factory.CreateClient();
        var name = NewName();
        using var first = await client.PostAsJsonAsync("/api/auth/register", Body(name));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var dupName = await client.PostAsJsonAsync("/api/auth/register",
            Body(name, email: NewName() + "@example.test"));
        using var dupEmail = await client.PostAsJsonAsync("/api/auth/register",
            Body(NewName(), email: name + "@example.test"));

        Assert.Equal(HttpStatusCode.Conflict, dupName.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dupEmail.StatusCode);
        Assert.Equal(await dupName.Content.ReadAsStringAsync(), await dupEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_ExtraRoleAndIdFields_AreIgnored()
    {
        using var client = factory.CreateClient();
        var name = NewName();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = name, email = name + "@example.test", displayName = "Студент 01",
            password = Password, role = "Administrator", id = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(name);
        Assert.NotNull(user);
        Assert.Equal(new[] { "Reporter" }, await users.GetRolesAsync(user!));
    }
}