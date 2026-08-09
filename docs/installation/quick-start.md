# PartyGame RC quick start

Requirements: macOS or Linux with `dotnet`, `node`, `curl`, `tar`, `shasum`, a private IPv4 LAN address, 100 MB free space plus runtime capacity, and port 5050 free. Download `partygame-1.0.0-rc.1.tar.gz`, verify it, then install with an explicit operator token:

```bash
scripts/verify-release-package.sh --package partygame-1.0.0-rc.1.tar.gz
PARTYGAME_OPERATOR_TOKEN='<32-or-more-character-secret>' \
  scripts/install-release.sh --package partygame-1.0.0-rc.1.tar.gz \
  --install-root /opt/partygame --runtime-root /var/lib/partygame \
  --host 192.168.1.20 --port 5050 --non-interactive
```

The installer verifies SHA-256, rejects unsafe archives, creates a mode-600 configuration file, migrates the empty database, starts PartyGame, waits for readiness, and runs smoke, security and diagnostics checks. It prints the Display and Admin URLs. Enter the same host URL in iOS Server Settings. Trusted LAN HTTP is intentionally explicit; use HTTPS outside a trusted private LAN.

Use `scripts/backup.sh`, `scripts/restore.sh` and `scripts/diagnose.sh` from the package directory for operations. Never put the operator token in a shell history, screenshot or support bundle.
