# PartyGame 1.0.0-rc.15

PartyGame RC15 is a self-contained LAN release candidate. It provides server-authoritative rooms, Display and Admin web clients, iOS participation, SignalR reconnect, and `PlayerSelection`, `TextAnswer`, `PhotoAnswer`, and `DrawingAnswer` questions.

RC11–RC15 focus on fixes found during manual testing on phones and TVs:

- **RC11:** the mobile Web Player recovers an in-progress drawing correctly.
- **RC12:** mobile and upload regressions are fixed, final selfie and photo uploads are covered by a benchmark, and the Display lobby fits a TV screen.
- **RC13:** single-choice actions (player selection and votes) are submitted on the first tap.
- **RC14:** player drawing controls and leaving the lobby are improved.
- **RC15:** a player who leaves the lobby is kept as an inactive record, so their stored media stays valid and the nickname can be reused in the room (database migration `RC15LobbyLeaveDurability`).

RC10 added responsive, stage-keyed motion with reduced-motion support, an optional question voice on Display, and the `manual-up.sh`, `manual-down.sh`, `manual-restart.sh` and `open-ios.sh` helpers for the local LAN workflow.

Install with the signed `partygame-1.0.0-rc.15.tar.gz` package and follow `docs/INSTALL.md`. The package keeps SQLite, media, backups, logs and support bundles in runtime storage outside the immutable release. Trusted LAN HTTP is an explicit operator choice; HTTPS is required outside that boundary. Operator access uses a 32+ character bearer token.

Backup, restore, migration, logs, correlation IDs and redacted support bundles are included. Physical iPhone/second-screen acceptance remains **pending**; it is documented in the release checklist and is required before a final `v1.0.0` production decision. Report faults with the release version, commit hash, redacted support bundle and reproduction steps; never attach a database, media tree or operator token.
