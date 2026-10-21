
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab02Endpoints
{
    private const int MaxTitleLength = 160;
    private const int MaxDescriptionLength = 4000;
    private const int MinDescriptionForHighSeverity = 40;

    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxIncidentAge = TimeSpan.FromDays(365);

    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", SearchAsync)
            .WithName("SearchIncidents");

        app.MapPost("/api/incidents", CreateAsync)
            .WithName("CreateIncident")
            .RequireAuthorization();
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        string? sortBy,
        SecureLabDbContext db,
        CancellationToken ct)
    {
        var search = q?.Trim() ?? "";

        if (search.Length > MaxTitleLength)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["q"] = [$"Пошуковий запит не може перевищувати {MaxTitleLength} символів."]
                });
        }

        // Сортування дозволене лише за визначеним переліком полів.
        var sortColumn = sortBy switch
        {
            null or "" or "createdAtUtc" => "created_at_utc",
            "severity" => "severity",
            "status" => "status",
            _ => null
        };

        if (sortColumn is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });
        }

        // Значення пошуку передається як параметр, а не вставляється в SQL.
        var pattern = $"%{search}%";

        var rows = await db.Incidents
            .FromSqlInterpolated($"""
                SELECT *
                FROM incidents
                WHERE title ILIKE {pattern}
                   OR description ILIKE {pattern}
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        var ordered = sortColumn switch
        {
            "severity" => rows.OrderBy(x => x.Severity),
            "status" => rows.OrderBy(x => x.Status),
            _ => rows.OrderByDescending(x => x.CreatedAtUtc)
        };

        var result = ordered
            .Take(50)
            .Select(row => new
            {
                row.Id,
                row.Title,
                row.Description,
                Severity = row.Severity.ToString(),
                Status = row.Status.ToString(),
                row.CreatedAtUtc
            });

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        CreateIncidentRequest request,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var errors = new Dictionary<string, string[]>();

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        if (title.Length == 0)
        {
            errors["title"] = ["Заголовок є обов'язковим."];
        }
        else if (title.Length > MaxTitleLength)
        {
            errors["title"] = [$"Заголовок не може перевищувати {MaxTitleLength} символів."];
        }

        if (description.Length == 0)
        {
            errors["description"] = ["Опис є обов'язковим."];
        }
        else if (description.Length > MaxDescriptionLength)
        {
            errors["description"] = [$"Опис не може перевищувати {MaxDescriptionLength} символів."];
        }

        IncidentSeverity severity = default;

var severityValid =
    !string.IsNullOrWhiteSpace(request.Severity)
    && !request.Severity.Contains(',')
    && Enum.TryParse<IncidentSeverity>(
        request.Severity,
        ignoreCase: true,
        out severity)
    && Enum.IsDefined(severity);

        if (!severityValid)
        {
            errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];
        }

        if (request.OccurredAtUtc is null)
        {
            errors["occurredAtUtc"] = ["Дата й час події є обов'язковими."];
        }
        else if (request.OccurredAtUtc.Value > now.Add(MaxFutureSkew))
        {
            errors["occurredAtUtc"] = ["Дата події не може бути більш ніж на 5 хвилин у майбутньому."];
        }
        else if (request.OccurredAtUtc.Value < now.Subtract(MaxIncidentAge))
        {
            errors["occurredAtUtc"] = ["Дата події не може бути старшою за 365 днів."];
        }

        if (severityValid
            && (severity is IncidentSeverity.High or IncidentSeverity.Critical)
            && description.Length > 0
            && description.Length < MinDescriptionForHighSeverity)
        {
            errors["description"] =
                [$"Для рівнів High і Critical опис має містити щонайменше {MinDescriptionForHighSeverity} символів."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var userExists = await db.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == ownerUserId, ct);

        if (!userExists)
        {
            return Results.Unauthorized();
        }

        var hasActiveDuplicate = await db.Incidents
            .AsNoTracking()
            .AnyAsync(
                item => item.Title == title && item.Status != IncidentStatus.Closed,
                ct);

        if (hasActiveDuplicate)
        {
            return Results.Problem(
                title: "Конфлікт даних",
                detail: "Активний інцидент із таким заголовком уже існує.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
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
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc));
    }
}

public sealed record CreateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc);
