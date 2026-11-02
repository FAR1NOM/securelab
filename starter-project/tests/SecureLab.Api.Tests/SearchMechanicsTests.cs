
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class SearchMechanicsTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private string GetSeedPassword()
    {
        return Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException(
                "Set local SeedUsers__Password outside Git.");
    }

    [Fact]
    public async Task Search_ReturnsJson()
    {
        using var client = await factory.LoginAsync(
            "alice",
            GetSeedPassword());

        using var response = await client.GetAsync(
            "/api/incidents/search?q=РЅР°РІС‡Р°Р»СЊРЅ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Search_ReporterCannotSeeOtherUsersIncidents()
    {
        using var client = await factory.LoginAsync(
            "alice",
            GetSeedPassword());

        using var response = await client.GetAsync(
            "/api/incidents/search?q=");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<
            List<IncidentListItemResponse>>();

        Assert.NotNull(results);
        Assert.Contains(results, item => item.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(results, item => item.Id == DbSeeder.SafeTextIncidentId);
        Assert.DoesNotContain(results, item => item.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task Search_AnonymousRequest_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/incidents/search?q=РЅР°РІС‡Р°Р»СЊРЅ");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_AnalystCanSeeOtherUsersIncidents()
    {
        using var client = await factory.LoginAsync(
            "morgan",
            GetSeedPassword());

        using var response = await client.GetAsync(
            "/api/incidents/search?q=");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<
            List<IncidentListItemResponse>>();

        Assert.NotNull(results);
        Assert.Contains(results, item => item.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(results, item => item.Id == DbSeeder.BobIncidentId);
        Assert.Contains(results, item => item.Id == DbSeeder.SafeTextIncidentId);
    }

    [Fact]
    public async Task Search_AdministratorIsForbidden()
    {
        using var client = await factory.LoginAsync(
            "admin",
            GetSeedPassword());

        using var response = await client.GetAsync(
            "/api/incidents/search?q=");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AnalystIsForbiddenAndIncidentIsNotCreated()
    {
        const string title = "Analyst forbidden creation test";

        using var client = await factory.LoginAsync(
            "morgan",
            GetSeedPassword());

        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title,
                description = "This incident must not be created by an analyst.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        Assert.False(await db.Incidents.AnyAsync(x => x.Title == title));
    }

    [Fact]
    public async Task Create_AdministratorIsForbiddenAndIncidentIsNotCreated()
    {
        const string title = "Administrator forbidden creation test";

        using var client = await factory.LoginAsync(
            "admin",
            GetSeedPassword());

        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title,
                description = "This incident must not be created by an administrator.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        Assert.False(await db.Incidents.AnyAsync(x => x.Title == title));
    }

    [Fact]
    public async Task SeveritySummary_ReporterSeesOnlyOwnIncidents()
    {
        using var client = await factory.LoginAsync(
            "alice",
            GetSeedPassword());

        using var response = await client.GetAsync(
            "/api/incidents/severity-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<
            List<IncidentSeveritySummaryResponse>>();

        Assert.NotNull(summary);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var expectedCount = await db.Incidents
            .CountAsync(x => x.OwnerUserId == DbSeeder.AliceId);

        Assert.Equal(expectedCount, summary.Sum(item => item.Count));
    }

    [Fact]
    public async Task SeveritySummary_AnalystSeesAllIncidents()
    {
        using var client = await factory.LoginAsync(
            "morgan",
            GetSeedPassword());

        using var response = await client.GetAsync(
            "/api/incidents/severity-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<
            List<IncidentSeveritySummaryResponse>>();

        Assert.NotNull(summary);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var expectedCount = await db.Incidents.CountAsync();

        Assert.Equal(expectedCount, summary.Sum(item => item.Count));
    }
}
