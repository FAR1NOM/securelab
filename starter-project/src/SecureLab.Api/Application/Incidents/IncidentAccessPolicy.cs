using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public static class IncidentAccessPolicy
{
    public static bool CanRead(ClaimsPrincipal user, Incident incident)
    {
        if (user.IsInRole(DbSeeder.AnalystRole))
            return true;

        return IsOwner(user, incident)
            && user.IsInRole(DbSeeder.ReporterRole);
    }

    public static bool CanUpdate(ClaimsPrincipal user, Incident incident)
    {
        if (user.IsInRole(DbSeeder.AnalystRole))
            return true;

        return IsOwner(user, incident)
            && user.IsInRole(DbSeeder.ReporterRole);
    }

    public static bool CanDelete(ClaimsPrincipal user)
    {
        return false;
    }

    public static bool CanChangeStatus(ClaimsPrincipal user)
    {
        return user.IsInRole(DbSeeder.AnalystRole);
    }

    public static bool CanChangeSeverity(ClaimsPrincipal user)
    {
        return user.IsInRole(DbSeeder.AnalystRole);
    }

    private static bool IsOwner(ClaimsPrincipal user, Incident incident)
    {
        return Guid.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            && incident.OwnerUserId == userId;
    }
}