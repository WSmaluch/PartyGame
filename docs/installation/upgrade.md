# Upgrade and rollback

Back up before an upgrade:

```bash
scripts/backup.sh --deploy-root /opt/partygame --backup-root /var/backups/partygame --maintenance
```

Install the next signed archive into the same install and runtime roots. Deployment creates a pre-migration backup when the schema changes, migrates before start, and restores the previous `current` release if startup or smoke fails. To roll back a schema-compatible release use `scripts/deploy-lan.sh --rollback <version>` with the normal root, host and port arguments. A schema-incompatible rollback is deliberately blocked; restore the pre-migration backup instead. Down-migrations are never automatic.
