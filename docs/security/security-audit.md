# Security audit — stage 8.4

## Endpoint map

| Surface | Access | Mutation | Protection |
| --- | --- | --- | --- |
| `/health`, `/health/ready`, `/api/system/version`, public content and media reads | public | no | bounded response, security headers |
| `/api/rooms` create/join/read | player/lobby | room state | server-issued reconnect token for every player-specific action; rate limits |
| player resume, profile photo, photo and drawing answers | player | yes | room + player + reconnect-token validation; upload validation |
| `/api/admin/content-packages/**` | operator | yes/read admin data | shared bearer-token endpoint filter and operator rate limit |
| `/hubs/game` | attached player or attached Display | player-only methods mutate | connection-to-room identity binding; player methods additionally verify reconnect token |

The Display can attach only to one concrete room per SignalR connection. It receives public state and cannot call player mutation methods because those require an active player assignment and valid reconnect token.

## Findings closed in 8.4

- Admin content endpoints were previously reachable without an operator boundary.
- A SignalR connection could change attachment role or identity after attach.
- Production HTTP lacked an explicit trusted-LAN acknowledgement.
- The release lacked an automated credential scan and security smoke check.

No operator token, reconnect token, cookie, multipart body, Authorization header, or SignalR query token is intentionally logged. Public links use configured `ReleaseRuntime:PublicBaseUrl`, never an untrusted Host or forwarded header.
