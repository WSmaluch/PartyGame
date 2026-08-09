# PartyGame 1.0.0-rc.1

PartyGame RC1 is a self-contained LAN release candidate. It provides server-authoritative rooms, Display and Admin web clients, iOS participation, SignalR reconnect, and `PlayerSelection`, `TextAnswer`, `PhotoAnswer`, and `DrawingAnswer` questions.

Install with the signed `partygame-1.0.0-rc.1.tar.gz` package and follow `docs/INSTALL.md`. The package keeps SQLite, media, backups, logs and support bundles in runtime storage outside the immutable release. Trusted LAN HTTP is an explicit operator choice; HTTPS is required outside that boundary. Operator access uses a 32+ character bearer token.

Backup, restore, migration, logs, correlation IDs and redacted support bundles are included. Physical iPhone/second-screen acceptance remains **pending**; it is documented in the release checklist and is required before a final `v1.0.0` production decision. Report faults with the release version, commit hash, redacted support bundle and reproduction steps; never attach a database, media tree or operator token.
