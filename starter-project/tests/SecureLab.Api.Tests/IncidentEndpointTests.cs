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

    private string GetSeedPassword()
    {
        return Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException(
                "Set local SeedUsers__Password outside Git.");
    }

    private async Task<HttpClient> LoginAsAliceAsync()
    {
        return await factory.LoginAsync("alice", GetSeedPassword());
    }

    private async Task<HttpClient> LoginAsBobAsync()
    {
        return await factory.LoginAsync("bob", GetSeedPassword());
    }

    private async Task<HttpClient> LoginAsMorganAsync()
    {
        return await factory.LoginAsync("morgan", GetSeedPassword());
    }

    [Fact]
    public async Task GetList_ReturnsOnlyOwnIncidents()
    {
        using var client = await LoginAsAliceAsync();

        var incidents = await client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.DoesNotContain(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task GetDetails_ReporterCannotAccessAnotherUsersIncident()
    {
        using var client = await LoginAsBobAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetails_AnalystCanAccessAnotherUsersIncident()
    {
        using var client = await LoginAsMorganAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDetails_AnalystCanReadAliceIncident()
    {
        using var client = await LoginAsMorganAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_UsesAuthenticatedUserAsOwner_WhenOwnerIdIsSpoofed()
    {
        using var client = await LoginAsBobAsync();

        var title = $"Owner spoof test {Guid.NewGuid():N}";
        var aliceId = DbSeeder.AliceId;
        var bobId = DbSeeder.BobId;
        Guid? incidentId = null;

        try
        {
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

            incidentId = json.RootElement.GetProperty("id").GetGuid();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

            var incident = await db.Incidents
                .AsNoTracking()
                .SingleAsync(x => x.Id == incidentId.Value);

            Assert.Equal(bobId, incident.OwnerUserId);
            Assert.NotEqual(aliceId, incident.OwnerUserId);
        }
        finally
        {
            if (incidentId.HasValue)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

                var incident = await db.Incidents
                    .SingleOrDefaultAsync(x => x.Id == incidentId.Value);

                if (incident is not null)
                {
                    db.Incidents.Remove(incident);
                    await db.SaveChangesAsync();
                }
            }
        }
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
        using var client = await LoginAsAliceAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var client = await LoginAsAliceAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));

        Assert.Equal(
            "Аліса Коваль",
            document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task GetDetails_ReporterCannotSeeInternalComments()
    {
        using var client = await LoginAsAliceAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var comments = document.RootElement.GetProperty("comments");

        Assert.Contains(
            comments.EnumerateArray(),
            comment => comment.GetProperty("text").GetString()
                == "Заголовки листа передано аналітику.");

        Assert.DoesNotContain(
            comments.EnumerateArray(),
            comment => comment.GetProperty("text").GetString()
                == "Внутрішня примітка для аналітиків.");
    }

    [Fact]
    public async Task GetDetails_AnalystCanSeeInternalComments()
    {
        using var client = await LoginAsMorganAsync();

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var comments = document.RootElement.GetProperty("comments");

        Assert.Contains(
            comments.EnumerateArray(),
            comment => comment.GetProperty("text").GetString()
                == "Внутрішня примітка для аналітиків.");
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetComment_ReporterCannotAccessCommentOfAnotherUsersIncident()
    {
        using var client = await LoginAsAliceAsync();

        var commentId = Guid.Parse(
            "30000000-0000-0000-0000-000000000002");

        using var response = await client.GetAsync(
            $"/api/incidentcomments/{commentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}