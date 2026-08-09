# Uninstall

`scripts/uninstall.sh --deploy-root … --host …` stops PartyGame and removes only release code and lifecycle configuration. It preserves SQLite, media, logs, support bundles and backups. Fixture-only data removal requires all three explicit flags: `--purge-data --confirm-purge --non-interactive`. Do not use purge on a production runtime until a verified backup exists.
