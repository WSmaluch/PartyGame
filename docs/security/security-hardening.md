# Security hardening

## Operator access

Set `PARTYGAME_OPERATOR_TOKEN` only in the deployment environment. It must be at least 32 characters and may not use a documentation placeholder. Production startup fails without it. The API accepts it only as `Authorization: Bearer <token>` and compares SHA-256 digests with a constant-time comparison.

The Admin Web asks for the token at runtime and holds it in JavaScript memory only. It is not part of `config.json`, environment-built frontend configuration, URLs, fragments, console output, or `localStorage`. A `401` clears the in-memory value and returns the user to the sign-in screen.

## Player and media boundaries

`X-Player-Token` remains the established player reconnect-token transport for REST. Tokens are checked together with room and player identity by `IRoomService`; IDs supplied by clients are never authorization by themselves. SignalR mutations require both the token check in the service and the active connection assignment.

Media names are server-generated. Storage validates declared MIME type, signature, decoded format, dimensions, byte limit, blank drawings, and atomic writes; a failed write removes temporary/partial files. Upload endpoints have a separate per-room/IP limiter.

## Request controls

Room traffic is limited per source IP and room. Uploads have a stricter limiter; operator attempts are limited per source IP. Rejections return `429` with `Retry-After: 60`. Limits are deliberately high enough for the normal Mixed Client flow and are not disabled in test profiles.

Responses receive `nosniff`, `no-referrer`, `DENY` framing, restrictive permissions policy, and a CSP restricted to same-origin scripts, connections and assets. Blob/data images are allowed only for the client media previews.
