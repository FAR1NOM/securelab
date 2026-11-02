
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentAuthorizationTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string NewStatus = "New";
    private const string TriagedStatus = "Triaged";

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

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        return await factory.LoginAsync("admin", GetSeedPassword());
    }

    [Fact]
    public async Task Create_ReporterCreatesIncidentAndServerAssignsOwner()
    {
        var title = $"Reporter owner assignment test {Guid.NewGuid()}";
        Guid? createdIncidentId = null;

        using var client = await LoginAsAliceAsync();

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/incidents",
                new
                {
                    title,
                    description = "Testing server-side owner assignment.",
                    severity = "Low",
                    occurredAtUtc = DateTimeOffset.UtcNow,
                    ownerUserId = DbSeeder.BobId
                });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.Content.ReadFromJsonAsync<
                CreatedIncidentResponse>();

            Assert.NotNull(created);

            createdIncidentId = created.Id;

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

            var incident = await db.Incidents
                .AsNoTracking()
                .SingleAsync(x => x.Id == created.Id);

            Assert.Equal(DbSeeder.AliceId, incident.OwnerUserId);
            Assert.NotEqual(DbSeeder.BobId, incident.OwnerUserId);
            Assert.Equal(title, incident.Title);
        }
        finally
        {
            if (createdIncidentId.HasValue)
            {
                using var cleanupScope = factory.Services.CreateScope();
                var cleanupDb = cleanupScope.ServiceProvider
                    .GetRequiredService<SecureLabDbContext>();

                await cleanupDb.Incidents
                    .Where(x => x.Id == createdIncidentId.Value)
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task Update_OwnerCanUpdateOwnIncident()
    {
        using var client = await LoginAsAliceAsync();

        using var response = await client.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}",
            new
            {
                title = "Updated by owner",
                description = "Updated description"
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReporterCannotUpdateAnotherUsersIncident()
    {
        using var client = await LoginAsBobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var before = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.AliceIncidentId);

        using var response = await client.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}",
            new
            {
                title = "Unauthorized update",
                description = "This update must be rejected"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var after = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.AliceIncidentId);

        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.OwnerUserId, after.OwnerUserId);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Severity, after.Severity);
    }

    [Fact]
    public async Task Delete_ReporterCannotDeleteOwnIncident()
    {
        using var client = await LoginAsAliceAsync();

        using var response = await client.DeleteAsync(
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReporterCannotDeleteAnotherUsersIncident()
    {
        using var client = await LoginAsBobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var before = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.AliceIncidentId);

        using var response = await client.DeleteAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var after = await db.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == DbSeeder.AliceIncidentId);

        Assert.NotNull(after);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.OwnerUserId, after.OwnerUserId);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Severity, after.Severity);
    }

    [Fact]
    public async Task ChangeStatus_ReporterIsForbidden()
    {
        using var client = await LoginAsAliceAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}/status",
            new
            {
                newStatus = TriagedStatus,
                note = "Reporter status change test"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_AdministratorIsForbiddenAndStatusRemainsUnchanged()
    {
        using var client = await LoginAsAdminAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.BobIncidentId);

        var originalStatus = incident.Status;

        using var response = await client.PostAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}/status",
            new
            {
                newStatus = TriagedStatus,
                note = "Administrator status change test"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var incidentAfterRequest = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.BobIncidentId);

        Assert.Equal(originalStatus, incidentAfterRequest.Status);
    }

    [Fact]
    public async Task ChangeStatus_AnalystCanMoveNewIncidentToTriaged()
    {
        using var client = await LoginAsMorganAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .SingleAsync(x => x.Id == DbSeeder.SafeTextIncidentId);

        var originalStatus = incident.Status;

        try
        {
            incident.Status = Enum.Parse<IncidentStatus>(NewStatus);

            await db.SaveChangesAsync();

            using var response = await client.PostAsJsonAsync(
                $"/api/incidents/{DbSeeder.SafeTextIncidentId}/status",
                new
                {
                    newStatus = TriagedStatus,
                    note = "Analyst status transition test"
                });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            await db.Entry(incident).ReloadAsync();

            Assert.Equal(IncidentStatus.Triaged, incident.Status);
        }
        finally
        {
            incident.Status = originalStatus;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Update_OverpostingCannotChangeOwnerOrStatus()
    {
        using var client = await LoginAsAliceAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.SafeTextIncidentId);

        using var response = await client.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}",
            new
            {
                title = "Overposting test",
                description = "Testing protected properties",
                ownerUserId = DbSeeder.BobId,
                status = TriagedStatus,
                severity = "Critical"
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updatedIncident = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.SafeTextIncidentId);

        Assert.Equal(incident.OwnerUserId, updatedIncident.OwnerUserId);
        Assert.Equal(incident.Status, updatedIncident.Status);
        Assert.Equal(incident.Severity, updatedIncident.Severity);
        Assert.Equal("Overposting test", updatedIncident.Title);
    }

    [Fact]
    public async Task GetDetails_ForeignAndUnknownIncidentsReturnSameResponse()
    {
        using var client = await LoginAsBobAsync();

        using var foreignResponse = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        using var unknownResponse = await client.GetAsync(
            $"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);

        Assert.Equal(
            foreignResponse.Content.Headers.ContentType?.MediaType,
            unknownResponse.Content.Headers.ContentType?.MediaType);

        var foreignJson = JsonNode.Parse(
            await foreignResponse.Content.ReadAsStringAsync());

        var unknownJson = JsonNode.Parse(
            await unknownResponse.Content.ReadAsStringAsync());

        foreignJson?.AsObject().Remove("traceId");
        unknownJson?.AsObject().Remove("traceId");

        foreignJson?.AsObject().Remove("requestId");
        unknownJson?.AsObject().Remove("requestId");

        Assert.Equal(
            foreignJson?.ToJsonString(),
            unknownJson?.ToJsonString());
    }

    [Fact]
    public async Task AuthorizationDenied_IsWrittenToAuditLogWithTraceId()
    {
        factory.LogProvider.Clear();

        using var client = await LoginAsAliceAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}/status",
            new
            {
                newStatus = TriagedStatus,
                note = "A-09 audit logging test"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var logEntry = Assert.Single(
            factory.LogProvider.Entries,
            entry => entry.Message.Contains(
                "authorization.denied",
                StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, logEntry.Level);

        Assert.True(
            logEntry.Properties.TryGetValue("TraceId", out var traceId));

        Assert.False(string.IsNullOrWhiteSpace(traceId?.ToString()));

        Assert.True(
            logEntry.Properties.TryGetValue("UserId", out var userId));

        Assert.False(string.IsNullOrWhiteSpace(userId?.ToString()));

        Assert.True(
            logEntry.Properties.TryGetValue("Resource", out var resource));

        Assert.False(string.IsNullOrWhiteSpace(resource?.ToString()));

        Assert.True(
            logEntry.Properties.TryGetValue("Action", out var action));

        Assert.False(string.IsNullOrWhiteSpace(action?.ToString()));
    }

    [Fact]
    public async Task ChangeSeverity_ReporterIsForbiddenAndSeverityRemainsUnchanged()
    {
        using var client = await LoginAsAliceAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.SafeTextIncidentId);

        var originalSeverity = incident.Severity;

        using var response = await client.PostAsJsonAsync(
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}/severity",
            new { newSeverity = "Critical" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var after = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.SafeTextIncidentId);

        Assert.Equal(originalSeverity, after.Severity);
    }

    [Fact]
    public async Task ChangeSeverity_AdministratorIsForbiddenAndSeverityRemainsUnchanged()
    {
        using var client = await LoginAsAdminAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.BobIncidentId);

        var originalSeverity = incident.Severity;

        using var response = await client.PostAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}/severity",
            new { newSeverity = "Critical" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var after = await db.Incidents
            .AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.BobIncidentId);

        Assert.Equal(originalSeverity, after.Severity);
    }

    [Fact]
    public async Task ChangeSeverity_AnalystCanChangeSeverity()
    {
        using var client = await LoginAsMorganAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .SingleAsync(x => x.Id == DbSeeder.SafeTextIncidentId);

        var originalSeverity = incident.Severity;

        try
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/incidents/{DbSeeder.SafeTextIncidentId}/severity",
                new { newSeverity = "Critical" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            await db.Entry(incident).ReloadAsync();

            Assert.Equal(IncidentSeverity.Critical, incident.Severity);
        }
        finally
        {
            incident.Severity = originalSeverity;
            await db.SaveChangesAsync();
        }
    }
}
