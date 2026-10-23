namespace FieldService.Api.Features.Auth.IssueSocketTicket;

public sealed record SocketTicketResponse(string Ticket, DateTimeOffset ExpiresAt);
