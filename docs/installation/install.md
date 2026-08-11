# INSTALL

See [quick start](quick-start.md) and [clean installation](clean-install.md). This package is installed with `scripts/install.sh`, uses `TrustedLanHttp` only after an explicit private-LAN host choice, and requires a recoverable operator token supplied by the operator. The installer checks its declared runtime tools before deployment: `dotnet`, `node`, `curl`, `shasum`, `tar`, `sqlite3`, and `unzip`. It does not require Homebrew or `ripgrep`.
