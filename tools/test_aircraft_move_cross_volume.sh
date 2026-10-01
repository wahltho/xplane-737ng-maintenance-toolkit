#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "The cross-volume test requires macOS hdiutil." >&2
  exit 1
fi

test_filter='FullyQualifiedName~AircraftMovePlatformTests.CrossVolume_'
if [[ "${1:-}" == "--full-suite" && $# == 1 ]]; then
  test_filter='FullyQualifiedName~LevelUp.NavTableUpdater.Core.Tests'
elif [[ $# == 0 ]]; then
  :
else
  echo "Usage: $0 [--full-suite]" >&2
  exit 1
fi

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
scratch="$(mktemp -d /private/tmp/mtk-move-volume.XXXXXX)"
mount_path="$scratch/volume"
attached=false
test_status=1
cleanup() {
  local status=$?
  if [[ "$test_status" != 0 ]]; then status="$test_status"; fi
  trap - EXIT INT TERM
  if [[ "$attached" == true ]]; then
    if ! hdiutil detach "$mount_path"; then
      echo "Test volume is still mounted at $mount_path; retained $scratch for safe cleanup." >&2
      exit 1
    fi
  fi
  rm -rf "$scratch"
  exit "$status"
}
trap cleanup EXIT
trap 'test_status=130; exit 130' INT
trap 'test_status=143; exit 143' TERM

hdiutil create -size 256m -type SPARSE -fs APFS -volname MTKMoveTest "$scratch/test.sparseimage"
mkdir "$mount_path"
hdiutil attach -nobrowse -mountpoint "$mount_path" "$scratch/test.sparseimage"
attached=true
export MTK_MOVE_TEST_VOLUME="$mount_path"
cd "$repo_root"
if dotnet test tests/LevelUp.NavTableUpdater.Core.Tests/LevelUp.NavTableUpdater.Core.Tests.csproj \
  --no-restore --configuration "${MTK_TEST_CONFIGURATION:-Debug}" \
  --filter "$test_filter" \
  --logger 'console;verbosity=normal'; then
  test_status=0
else
  test_status=$?
fi
exit "$test_status"
