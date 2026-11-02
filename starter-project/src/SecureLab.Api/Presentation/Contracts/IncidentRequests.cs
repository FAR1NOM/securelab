namespace SecureLab.Api.Presentation.Contracts;

public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description);

public sealed record ChangeIncidentStatusRequest(
    string? NewStatus,
    string? Note);

public sealed record ChangeIncidentSeverityRequest(
    string? NewSeverity);