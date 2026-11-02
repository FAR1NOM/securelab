using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

public sealed class RateLimitingTests
{
    private const string PasswordVariable = "SeedUsers__Password";

    private static string GetSeedPassword()
    {
        return Environment.GetEnvironmentVariable(PasswordVariable)
            ?? throw new InvalidOperationException(
                "Set local SeedUsers__Password outside Git.");
    }

    [Fact]
    public async Task Search_ExceedingRateLimit_Returns429()
    {
        using var factory = new RateLimitTestFactory();
        using var client = await factory.LoginAsync(
            "alice",
            GetSeedPassword());

        using var first = await client.GetAsync(
            "/api/incidents/search?q=");

        using var second = await client.GetAsync(
            "/api/incidents/search?q=");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            second.StatusCode);
    }

    [Fact]
    public async Task Create_ExceedingRateLimit_Returns429()
    {
        using var factory = new RateLimitTestFactory();
        using var client = await factory.LoginAsync(
            "alice",
            GetSeedPassword());

        var title = $"Rate limit test {Guid.NewGuid()}";

        using var first = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title,
                description = "Rate limiting integration test incident.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow
            });

        using var second = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title = $"{title} second",
                description = "Second request for rate limiting test.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow
            });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            second.StatusCode);
    }

    private sealed class RateLimitTestFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["RateLimit:Search:PermitLimit"] = "1",
                        ["RateLimit:Search:WindowSeconds"] = "60",
                        ["RateLimit:Create:PermitLimit"] = "1",
                        ["RateLimit:Create:WindowSeconds"] = "60"
                    });
            });
        }
    }
}