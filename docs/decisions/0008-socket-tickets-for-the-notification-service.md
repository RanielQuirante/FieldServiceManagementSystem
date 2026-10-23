# 0008. Socket tickets for the notification service

## Status

Accepted, 2026-10-23.

## Context

The dashboard receives live job updates from the notification service over Socket.IO. The service must only accept connections from signed-in administrators whose sessions are still live, but it is a separate process with no database and no business rules, and it should hold nothing that would let an attacker act on the API if the service were compromised.

The usual credentials do not fit. Sending the API's access token to the service would put a token that the API accepts in a second process, and a compromised service could replay it against the API for up to 15 minutes. Authenticating the socket with the refresh cookie would expose the WebSocket to cross-site hijacking and would still need the service to understand API sessions. A browser cannot set an `Authorization` header on a WebSocket upgrade, and a credential in the URL ends up in proxy and access logs.

## Decision

- **Issuance.** The API issues a socket ticket at `POST /api/auth/socket-tickets`. The endpoint requires the Administrator policy, and before issuing it checks the access token's session: the session must exist, belong to the caller, be unrevoked, be inside its idle and absolute expiry, and belong to an active user. Anything else gets 401 `auth.session-expired`. The endpoint allows 30 requests a minute per user and answers with `Cache-Control: no-store`.
- **Ticket format.** The ticket is a JSON Web Token signed with HS256 and a separate key, `Auth:SocketTicketKey`, of 32 bytes or more. Its claims are `iss` `fieldservice-api`, `aud` `fieldservice-notifications`, `sub` (the user id), `role`, `jti` (a new UUIDv7), `iat`, `nbf` equal to `iat`, and `exp` 60 seconds later. The API refuses to start when the ticket key equals the access-token key, is a published test key or is patterned. The API's own token validation rejects a ticket, because its audience and key differ.
- **Transport.** The socket uses the WebSocket transport only. Once the WebSocket is open, the dashboard fetches a fresh ticket for the handshake and sends it only in the `auth` member of the Socket.IO CONNECT packet: never in a URL, a header or a cookie.
- **Verification.** The notification service checks the browser's `Origin` against `SOCKET_ALLOWED_ORIGINS` at the WebSocket upgrade, then verifies the ticket in the handshake with `SOCKET_TICKET_KEY`: HS256 only, the issuer and audience above, at most 60 seconds old with 5 seconds of clock tolerance, all six claims present, a UUID `sub` and the `Administrator` role. Each `jti` is accepted once; verified ids are kept for 120 seconds, at most 10,000 of them, and an entry is evicted only once its ticket can no longer verify. A refusal is reported to the client as `ticket_missing`, `ticket_invalid` or `forbidden` and logged without the ticket.
- **Session cap.** The service disconnects every socket after `SOCKET_SESSION_MAX_SECONDS` (900 by default). The dashboard reconnects with a new ticket, so the session check runs again regularly.
- **Shared test vector.** `docs/contracts/events/socket-ticket-test-vector.json` holds a published key, fixed claims and the expected token. The API's issuer must produce the token and the service's verifier must accept it. Both services refuse to start with the published key.

## Consequences

- The notification service holds no credential the API accepts. A ticket stolen from a browser works only at the notification service, which serves identifiers and statuses only; it works once, if it is used before the dashboard uses it, and for about a minute (60 seconds plus 5 seconds of clock tolerance).
- Revoking a session stops new tickets at once, but an open socket stays connected until its session cap, at most 15 minutes by default; `docs/security.md` lists this among the accepted limitations.
- Every handshake costs one API request, which the per-user limit bounds; attempts that fail before the WebSocket opens cost none.
- The ticket key is a second signing secret to manage. It must be changed in both services together; during the change, handshakes fail with `ticket_invalid` for up to about a minute and clients retry with fresh tickets.
