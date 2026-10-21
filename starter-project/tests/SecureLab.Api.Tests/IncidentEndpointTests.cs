using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        var incidents = await _client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }
[Fact]
public async Task Create_UsesAuthenticatedUserAsOwner_WhenOwnerIdIsSpoofed()
{
    var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set local SeedUsers__Password outside Git.");

    using var client = await factory.LoginAsync("bob", password);

    var title = $"Owner spoof test {Guid.NewGuid():N}";
    var aliceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var bobId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    using var response = await client.PostAsJsonAsync(
        "/api/incidents",
        new
        {
            title,
            description = "Testing that the authenticated user owns the new incident.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow,
            ownerUserId = aliceId
        });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    using var json = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync());

    var incidentId = json.RootElement.GetProperty("id").GetGuid();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

    var incident = await db.Incidents
        .AsNoTracking()
        .SingleAsync(x => x.Id == incidentId);

    Assert.Equal(bobId, incident.OwnerUserId);
    Assert.NotEqual(aliceId, incident.OwnerUserId);
}
    [Fact]
public async Task Create_AnonymousRequest_ReturnsUnauthorized()
{
    var request = new
    {
        Title = "Anonymous test incident",
        Description = "This incident must not be created without authentication.",
        Severity = "Low"
    };

    using var response = await _client.PostAsJsonAsync(
        "/api/incidents",
        request);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var response = await _client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Equal("Аліса Коваль", document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }
}
