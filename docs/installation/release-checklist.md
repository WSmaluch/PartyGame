# Release and physical-device checklist

Automated RC acceptance covers package integrity, clean installation, upgrade, restore, security, diagnostics, support bundle and Mixed Client E2E. Before final production approval, manually record the following on the intended LAN:

- iPhone opens the server URL, joins a room, reconnects after Wi-Fi toggle and after app relaunch.
- A second computer or tablet opens Admin and Display over LAN.
- TV/Display remains attached through a game, reconnect and host restart.
- Play profile photo, drawing, vote and final ranking; verify all media survives restart.
- Confirm operator token is never visible in Admin UI, logs or exported evidence.

If physical devices were unavailable, record **Manual physical-device acceptance: pending**. This blocks final `v1.0.0`, not automated RC publication.
