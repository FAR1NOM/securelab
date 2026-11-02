using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents");

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails");

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateIncident");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident");

        group.MapPost("/{id:guid}/status", ChangeStatusAsync)
            .WithName("ChangeIncidentStatus");

        group.MapPost("/{id:guid}/severity", ChangeSeverityAsync)
            .WithName("ChangeIncidentSeverity");

        endpoints.MapGet(
                "/api/incidentcomments/{id:guid}",
                GetCommentAsync)
            .WithTags("Incident Comments")
            .RequireAuthorization()
            .WithName("GetIncidentComment");

        return endpoints;
    }

    private static void LogAuthorizationDenied(
        ILogger logger,
        HttpContext context,
        ClaimsPrincipal user,
        string resource,
        string action)
    {
        logger.LogWarning(
            "authorization.denied TraceId={TraceId} UserId={UserId} Resource={Resource} Action={Action}",
            context.TraceIdentifier,
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            resource,
            action);
    }

    private static async Task<IResult> GetListAsync(
        ClaimsPrincipal user,
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;

        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(
                    status, true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["status"] =
                        [
                            "Допустимые значения: New, Triaged, InProgress, Resolved, Closed."
                        ]
                    });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(
            await queries.GetListAsync(
                user, parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        ClaimsPrincipal user,
        IncidentQueries queries,
        HttpContext context,
        ILogger<IncidentQueries> logger,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(
            id, user, cancellationToken);

        if (incident is null)
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "read");

            return Results.Problem(
                title: "Инцидент не найден",
                detail: "Инцидент не существует или недоступен.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(incident);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        ClaimsPrincipal user,
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;

        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(
                    status, true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["status"] =
                        [
                            "Допустимые значения: New, Triaged, InProgress, Resolved, Closed."
                        ]
                    });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(
            await queries.GetSeveritySummaryAsync(
                user, parsedStatus, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        HttpContext context,
        ILogger<IncidentQueries> logger,
        CancellationToken ct)
    {
        var incident = await db.Incidents
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (incident is null || !IncidentAccessPolicy.CanRead(user, incident))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "update");

            return Results.NotFound();
        }

        if (!IncidentAccessPolicy.CanUpdate(user, incident))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "update");

            return Results.Forbid();
        }

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        var errors = new Dictionary<string, string[]>();

        if (title.Length == 0 || title.Length > 160)
        {
            errors["title"] =
            [
                "Заголовок должен содержать от 1 до 160 символов."
            ];
        }

        if (description.Length == 0 || description.Length > 4000)
        {
            errors["description"] =
            [
                "Описание должно содержать от 1 до 4000 символов."
            ];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        incident.Title = title;
        incident.Description = description;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        HttpContext context,
        ILogger<IncidentQueries> logger,
        CancellationToken ct)
    {
        var incident = await db.Incidents
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (incident is null || !IncidentAccessPolicy.CanRead(user, incident))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "delete");

            return Results.NotFound();
        }

        if (!IncidentAccessPolicy.CanDelete(user))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "delete");

            return Results.Forbid();
        }

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id,
        ChangeIncidentStatusRequest request,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        HttpContext context,
        ILogger<IncidentQueries> logger,
        CancellationToken ct)
    {
        if (!IncidentAccessPolicy.CanChangeStatus(user))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "change-status");

            return Results.Forbid();
        }

        var incident = await db.Incidents
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (incident is null)
            return Results.NotFound();

        if (!Enum.TryParse<IncidentStatus>(
                request.NewStatus,
                true,
                out var newStatus)
            || !Enum.IsDefined(newStatus))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["newStatus"] =
                    [
                        "Недопустимое значение статуса."
                    ]
                });
        }

        if (incident.Status != IncidentStatus.New
            || newStatus != IncidentStatus.Triaged)
        {
            return Results.Conflict(new
            {
                message = "Недопустимый переход статуса."
            });
        }

        var userIdClaim =
            user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        var note = request.Note?.Trim();

        if (string.IsNullOrWhiteSpace(note) || note.Length > 1000)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["note"] =
                    [
                        "Примечание обязательно и не должно превышать 1000 символов."
                    ]
                });
        }

        var oldStatus = incident.Status;

        incident.Status = newStatus;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        db.IncidentStatusHistory.Add(
            new IncidentStatusHistory
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                ChangedByUserId = userId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Note = note,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ChangeSeverityAsync(
        Guid id,
        ChangeIncidentSeverityRequest request,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        HttpContext context,
        ILogger<IncidentQueries> logger,
        CancellationToken ct)
    {
        if (!IncidentAccessPolicy.CanChangeSeverity(user))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident", "change-severity");

            return Results.Forbid();
        }

        var incident = await db.Incidents
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (incident is null)
            return Results.NotFound();

        if (!Enum.TryParse<IncidentSeverity>(
                request.NewSeverity,
                true,
                out var newSeverity)
            || !Enum.IsDefined(newSeverity))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["newSeverity"] =
                    [
                        "Допустимые значения: Low, Medium, High, Critical."
                    ]
                });
        }

        incident.Severity = newSeverity;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        HttpContext context,
        ILogger<IncidentQueries> logger,
        CancellationToken ct)
    {
        var comment = await db.IncidentComments
            .Include(x => x.Incident)
            .Include(x => x.Author)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (comment is null
            || !IncidentAccessPolicy.CanRead(user, comment.Incident)
            || (comment.IsInternal
                && !user.IsInRole(DbSeeder.AnalystRole)))
        {
            LogAuthorizationDenied(
                logger, context, user, "incident-comment", "read");

            return Results.NotFound();
        }

        return Results.Ok(
            new IncidentCommentResponse(
                comment.Id,
                comment.Author.DisplayName,
                comment.Text,
                comment.CreatedAtUtc));
    }
}