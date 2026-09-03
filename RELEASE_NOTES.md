# PartyGame 1.0.0-rc.10

PartyGame RC10 is a self-contained LAN release candidate. It provides server-authoritative rooms, Display and Admin web clients, iOS participation, SignalR reconnect, and `PlayerSelection`, `TextAnswer`, `PhotoAnswer`, and `DrawingAnswer` questions.

RC10 is a polish and manual-test reliability release. Player and Display now use responsive, stage-keyed motion with reduced-motion support; the Player question header no longer relies on a colliding negative margin; submission, timer, selection, ranking, and Final Presentation feedback are smoother. Hosts can optionally enable a browser/system question voice on Display. The new `manual-up.sh`, `manual-down.sh`, `manual-restart.sh`, and `open-ios.sh` helpers provide the short local LAN workflow without weakening certification scripts.

Install with the signed `partygame-1.0.0-rc.10.tar.gz` package and follow `docs/INSTALL.md`. The package keeps SQLite, media, backups, logs and support bundles in runtime storage outside the immutable release. Trusted LAN HTTP is an explicit operator choice; HTTPS is required outside that boundary. Operator access uses a 32+ character bearer token.

Backup, restore, migration, logs, correlation IDs and redacted support bundles are included. Physical iPhone/second-screen acceptance remains **pending**; it is documented in the release checklist and is required before a final `v1.0.0` production decision. Report faults with the release version, commit hash, redacted support bundle and reproduction steps; never attach a database, media tree or operator token.
