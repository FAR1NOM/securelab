using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

public sealed class LoginRateLimitTests
{
    [Fact]
    public async Task Login_ExceedingLimit_Returns429_ThenRecovers()
    {
        await using var factory = new RateLimitApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        async Task<HttpStatusCode> AttemptLogin()
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    userName = "alice",
                    password = "DefinitelyWrongPassword123!"
                });

            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptLogin());
        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptLogin());
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            await AttemptLogin());

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptLogin());
    }

    private sealed class RateLimitApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["RateLimit:Login:PermitLimit"] = "2",
                        ["RateLimit:Login:WindowSeconds"] = "2"
                    });
            });
        }
    }
}
