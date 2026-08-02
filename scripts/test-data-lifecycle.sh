#!/usr/bin/env bash
# Disposable integration coverage for the data lifecycle scripts. It deliberately
# creates no files inside the repository or an operator deployment.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DLL="${PARTYGAME_DATA_API_DLL:-$SCRIPT_DIR/../server/PartyGame.Api/bin/Release/net10.0/PartyGame.Api.dll}"
[[ -f "$API_DLL" ]] || { echo "build the Release API before running this test" >&2; exit 64; }

ROOT="$(mktemp -d "${TMPDIR:-/tmp}/partygame-data-lifecycle.XXXXXX")"
DEPLOY="$ROOT/deploy"; BACKUPS="$ROOT/backups"; DB="$DEPLOY/runtime/database/partygame.db"; MEDIA="$DEPLOY/runtime/media"
cleanup() { rm -rf "$ROOT"; }
trap cleanup EXIT
mkdir -p "$DEPLOY/runtime/database" "$MEDIA" "$BACKUPS"

ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__PartyGame="Data Source=$DB" MediaStorage__RootPath="$MEDIA" dotnet "$API_DLL" migrate >/dev/null
# The media copier needs records of both kinds. Foreign keys are intentionally
# disabled only for this isolated storage-format fixture; the lifecycle code only
# relies on the opaque keys and hashes, exactly as a real snapshot does.
photo_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"; drawing_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
mkdir -p "$MEDIA/photos" "$MEDIA/drawings"
printf 'photo-media' > "$MEDIA/photos/$photo_id.jpg"
printf 'drawing-media' > "$MEDIA/drawings/$drawing_id.png"
photo_sha="$(shasum -a 256 "$MEDIA/photos/$photo_id.jpg" | awk '{print $1}')"
drawing_sha="$(shasum -a 256 "$MEDIA/drawings/$drawing_id.png" | awk '{print $1}')"
sqlite3 "$DB" <<SQL
PRAGMA foreign_keys=OFF;
PRAGMA journal_mode=WAL;
INSERT INTO MediaAssets (Id,ByteLength,ContentType,CreatedAtUtc,DisplayStorageKey,Height,MediaKind,PlayerId,QuestionInstanceId,RoomId,Sha256,StorageProvider,ThumbnailStorageKey,Width)
VALUES ('$photo_id',11,'image/jpeg','2026-08-02T00:00:00+00:00','photos/$photo_id.jpg',1,0,'$photo_id',NULL,'$photo_id','$photo_sha','LocalFileSystem','photos/$photo_id.jpg',1);
INSERT INTO MediaAssets (Id,ByteLength,ContentType,CreatedAtUtc,DisplayStorageKey,Height,MediaKind,PlayerId,QuestionInstanceId,RoomId,Sha256,StorageProvider,ThumbnailStorageKey,Width)
VALUES ('$drawing_id',13,'image/png','2026-08-02T00:00:00+00:00','drawings/$drawing_id.png',1,1,'$drawing_id',NULL,'$drawing_id','$drawing_sha','LocalFileSystem','drawings/$drawing_id.png',1);
SQL

"$SCRIPT_DIR/backup-data.sh" --deploy-root "$DEPLOY" --backup-root "$BACKUPS" --name baseline --online
"$SCRIPT_DIR/verify-backup.sh" "$BACKUPS/baseline"

before="$(shasum -a 256 "$DB" | awk '{print $1}')"
"$SCRIPT_DIR/restore-data.sh" --deploy-root "$DEPLOY" --backup "$BACKUPS/baseline" --backup-root "$BACKUPS" --dry-run
[[ "$before" == "$(shasum -a 256 "$DB" | awk '{print $1}')" ]] || { echo "dry run changed database" >&2; exit 1; }

sqlite3 "$DB" 'CREATE TABLE DataLifecycleProbe (Id INTEGER PRIMARY KEY); INSERT INTO DataLifecycleProbe VALUES (1);'
changed_before_rollback="$(shasum -a 256 "$DB" | awk '{print $1}')"
# The failure toggle is a test-only process environment value understood by the
# shell script; it exercises the same rollback branch as a failed second move.
if PARTYGAME_RESTORE_TEST_FAIL_AFTER_DATABASE_SWAP=true "$SCRIPT_DIR/restore-data.sh" --deploy-root "$DEPLOY" --backup "$BACKUPS/baseline" --backup-root "$BACKUPS"; then
  echo "injected restore swap failure was accepted" >&2; exit 1
fi
[[ "$changed_before_rollback" == "$(shasum -a 256 "$DB" | awk '{print $1}')" ]] || { echo "failed restore did not roll back database" >&2; exit 1; }
[[ -d "$MEDIA" ]] || { echo "failed restore did not roll back media" >&2; exit 1; }
"$SCRIPT_DIR/restore-data.sh" --deploy-root "$DEPLOY" --backup "$BACKUPS/baseline" --backup-root "$BACKUPS"
[[ "$(sqlite3 "$DB" "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='DataLifecycleProbe';")" == 0 ]] || { echo "restore did not replace changed database" >&2; exit 1; }
[[ "$(shasum -a 256 "$MEDIA/photos/$photo_id.jpg" | awk '{print $1}')" == "$photo_sha" ]] || { echo "photo media hash changed" >&2; exit 1; }
[[ "$(shasum -a 256 "$MEDIA/drawings/$drawing_id.png" | awk '{print $1}')" == "$drawing_sha" ]] || { echo "drawing media hash changed" >&2; exit 1; }

cp -R "$BACKUPS/baseline" "$BACKUPS/checksum-mismatch"
printf x >> "$BACKUPS/checksum-mismatch/database/partygame.db"
if "$SCRIPT_DIR/verify-backup.sh" "$BACKUPS/checksum-mismatch" >/dev/null 2>&1; then echo "checksum corruption was accepted" >&2; exit 1; fi

mkdir -p "$DEPLOY/runtime/operations/data-operation.lock"
printf 'operation=test\npid=%s\nstartedAtUtc=2026-08-02T00:00:00Z\napplicationVersion=test\n' "$$" > "$DEPLOY/runtime/operations/data-operation.lock/metadata"
if "$SCRIPT_DIR/backup-data.sh" --deploy-root "$DEPLOY" --backup-root "$BACKUPS" --name locked --online >/dev/null 2>&1; then echo "active lifecycle lock was accepted" >&2; exit 1; fi
rm -rf "$DEPLOY/runtime/operations/data-operation.lock"

"$SCRIPT_DIR/prune-backups.sh" --backup-root "$BACKUPS" --deploy-root "$DEPLOY" --keep-last 1 --keep-days 0 --dry-run
find "$BACKUPS" -mindepth 1 -maxdepth 1 -type d -name '*-pre-restore' -exec touch -t 202001010000 {} +
"$SCRIPT_DIR/prune-backups.sh" --backup-root "$BACKUPS" --deploy-root "$DEPLOY" --keep-last 1 --keep-days 0
echo "PartyGame data lifecycle integration PASS"
