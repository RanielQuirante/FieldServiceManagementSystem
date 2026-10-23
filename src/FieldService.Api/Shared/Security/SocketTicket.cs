namespace FieldService.Api.Shared.Security;

public sealed record SocketTicket(string Value, Guid TicketId, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"SocketTicket {{ TicketId = {TicketId}, ExpiresAt = {ExpiresAt:O} }}";
}
