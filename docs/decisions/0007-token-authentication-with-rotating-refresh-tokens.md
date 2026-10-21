# 0007. Token authentication with rotating refresh tokens

## Status

Accepted, 2026-10-21.

## Context

Two clients sign in to one API. Administrators use the dashboard in a browser, served from the same origin as the API. Technicians use the mobile app, which keeps a session for days on a personal device. Both need short-lived credentials on every request and a way to stay signed in without typing a password again. A stolen long-lived credential must be detectable, and signing out must end the session on the server.

The browser cannot hold a long-lived secret in script-readable storage without exposing it to any script that runs on the page. The mobile app has secure platform storage but no cookies. The API is stateless per request, and its database is the only store both clients share.

## Decision

- **Access tokens.** The API issues a JSON Web Token signed with HS256 and a 32-byte or longer key (`Auth:AccessTokenKey`) that only the API holds. Issuer and audience are both `fieldservice-api`, the lifetime is 15 minutes, and validation allows 30 seconds of clock skew and no other algorithm. The token carries the user id, the role and the session id. Clients send it as a bearer token and never store it beyond memory.
- **Sessions and refresh tokens.** Each sign-in creates a session row and a first refresh token: an opaque 256-bit random value of which the database stores only the SHA-256 hash. A web session expires after 60 minutes idle or 12 hours in total, and a mobile session after 7 days idle or 30 days in total.
- **Rotation with reuse detection.** Every refresh consumes the presented token with a conditional update and issues a successor, in one transaction that locks the session row. A token presented again less than 30 seconds after its rotation, while its successor is still unused, counts as a retry after a lost response and gets a fresh successor. Any other reuse of a consumed token revokes the whole session and is logged as a security event.
- **Browser channel.** The web client's refresh token travels only in the `fs_refresh` cookie: HttpOnly, SameSite=Strict, Secure, limited to the path `/api/auth`, with a lifetime that never passes the session's expiry. Refresh and sign-out with the cookie also require the header `X-FieldService-Csrf: 1`. The dashboard restores the session through a refresh on page load, refreshes about a minute before the access token expires, serialises refreshes across tabs with a Web Lock and announces sign-out to other tabs.
- **Mobile channel.** The mobile client receives its refresh token in the response body, keeps it in the platform's secure storage and sends it in the request body; it needs no cookie and no CSRF header. A request that fails with 401 triggers one shared refresh and one retry.
- **Client binding.** The sign-in request names its client. The web client accepts only administrators and the mobile client only technicians; the wrong account type is refused after the password verifies, and a token presented through the other client's channel is treated as unknown.
- **Sign-out** revokes the session and always answers 204, so it never reveals whether a credential was valid.

## Consequences

- A stolen access token is useful for at most 15 minutes, and a stolen refresh token is caught the moment either party uses it after the other.
- No script on the dashboard can read the refresh token, and the CSRF header with SameSite=Strict keeps other sites from using the cookie.
- The API reads the database on every refresh, not on every request; revocation takes effect at the next refresh, so an access token already issued stays valid until it expires. Ending a user's access at once needs the operator procedure in `docs/security.md`.
- Rotating `Auth:AccessTokenKey` signs every client out of its current access token only; clients recover silently through their refresh tokens, which live in the database.
- The refresh path is the most complex code in the API, so its rotation, grace and reuse rules have dedicated tests, and the cookie attributes and CSRF behaviour are checked attribute by attribute.
