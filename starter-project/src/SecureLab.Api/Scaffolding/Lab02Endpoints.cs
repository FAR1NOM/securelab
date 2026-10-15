using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        app.MapPost("/api/incidents", CreateAsync);
    }

    private const int MaxTitleLength = 160;
    private const int MaxDescriptionLength = 4000;
    private const int MinDescriptionForHighSeverity = 40;
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);
    // T-10 (власне додаткове правило): подія не може бути старшою за 365 днів.
    private static readonly TimeSpan MaxIncidentAge = TimeSpan.FromDays(365);

    private static async Task<IResult> CreateAsync(
        CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var errors = new Dictionary<string, string[]>();
        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        if (title.Length == 0)
        {
            errors["title"] = ["Заголовок обов'язковий."];
        }
        else if ((request.Title?.Length ?? 0) > MaxTitleLength)
        {
            errors["title"] = [$"Заголовок не може перевищувати {MaxTitleLength} символів."];
        }

        if (description.Length == 0)
        {
            errors["description"] = ["Опис обов'язковий."];
        }
        else if ((request.Description?.Length ?? 0) > MaxDescriptionLength)
        {
            errors["description"] = [$"Опис не може перевищувати {MaxDescriptionLength} символів."];
        }

        IncidentSeverity severity = default;
        var severityValid = request.Severity is not null
            && !request.Severity.Contains(',')
            && Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out severity)
            && Enum.IsDefined(severity);
        if (!severityValid)
        {
            errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];
        }

        if (request.OccurredAtUtc is null)
        {
            errors["occurredAtUtc"] = ["Дата й час події обов'язкові."];
        }
        else if (request.OccurredAtUtc.Value > now.Add(MaxFutureSkew))
        {
            errors["occurredAtUtc"] = ["Дата події не може бути в майбутньому (допуск 5 хвилин)."];
        }
        else if (request.OccurredAtUtc.Value < now - MaxIncidentAge)
        {
            errors["occurredAtUtc"] = ["Дата події не може бути старшою за 365 днів."];
        }

        if (severityValid
            && (severity is IncidentSeverity.High or IncidentSeverity.Critical)
            && !errors.ContainsKey("description")
            && description.Length < MinDescriptionForHighSeverity)
        {
            errors["description"] =
                [$"Для High і Critical опис має містити щонайменше {MinDescriptionForHighSeverity} символів."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var hasActiveDuplicate = await db.Incidents.AsNoTracking()
            .AnyAsync(item => item.Title == title && item.Status != IncidentStatus.Closed, ct);
        if (hasActiveDuplicate)
        {
            return Results.Problem(
                title: "Конфлікт даних",
                detail: "Активний інцидент з таким заголовком уже існує.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            OwnerUserId = DbSeeder.AliceId,
            Title = title,
            Description = description,
            Severity = severity,
            Status = IncidentStatus.New,
            OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/incidents/{incident.Id}",
            new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(), incident.Status.ToString(),
                incident.OccurredAtUtc, incident.CreatedAtUtc));
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);

public sealed record IncidentSearchItem(
    Guid Id, string Title, string Description, string Severity, string Status,
    DateTimeOffset CreatedAtUtc);
