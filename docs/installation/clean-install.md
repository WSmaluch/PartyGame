# Clean installation

Run package verification before installation. `install-release.sh` requires `--package`, `--install-root`, `--host` and, in `--non-interactive` mode, `PARTYGAME_OPERATOR_TOKEN`. `--runtime-root` defaults to `<install-root>/runtime`, which is outside `releases/`; give it a separate persistent volume when desired.

The installer leaves runtime untouched on an error and deploys releases atomically through `current`. It verifies `/health`, `/health/ready`, `/api/system/version`, Display, Admin, SignalR, the security smoke and a redacted support bundle. Stop the host with `scripts/stop.sh` before maintenance.
