# BACKUP AND RESTORE

Use `scripts/backup.sh` to create a checksummed SQLite-and-media backup and `scripts/restore.sh` only while PartyGame is stopped. Restore verifies the archive, creates a pre-restore backup, swaps data atomically and rolls back on failure. See [upgrade](upgrade.md) for pre-migration backup and rollback policy.
