# TLS and networking

Production uses HTTPS by default. Configure Kestrel through the standard ASP.NET Core `ASPNETCORE_URLS`/certificate environment settings, and set `PARTYGAME_PUBLIC_BASE_URL` to the validated HTTPS public URL. Certificate passwords and private keys must remain deployment secrets, never repository files.

Plain HTTP is a deliberate exception named **TrustedLanHttp**. A Production process using an `http://` public or listening URL fails to start unless `PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true` is set. This is only acceptable on a trusted private LAN; the application writes a warning without any secret. Set `PARTYGAME_ENABLE_HSTS=true` only after HTTPS and any redirect are verified.

CORS in Production accepts only explicit origins from `PARTYGAME_ALLOWED_ORIGINS`; wildcard origins are rejected by startup validation. Same-origin `/display`, `/admin`, `/api`, and `/hubs/game` requests need no CORS header. The app does not enable forwarded-header middleware, so unconfigured `X-Forwarded-*` values are ignored. Reverse-proxy deployments must configure known proxy addresses/networks and a forward limit before enabling forwarded headers.
