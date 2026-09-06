#!/usr/bin/env bash
# The engine, laid out the way a release lays it out, so the broker-contract suite can start one in
# its own process.
#
#   ./ci/stage-the-engine.sh [platform-checkout] [destination]
#
# Defaults to a platform checkout beside this one and to .vos-engine here, which is where the suite's
# project file looks when nothing names a staged engine. The build stages its own and points
# VosRelease at it instead.
#
# The layout is not spelled out here. The platform's own script writes it, and a second description
# of it would be the thing that drifts — which is the whole reason this suite exists.
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
platform="${1:-$(cd "$repository/.." && pwd)/VillageOS}"
destination="${2:-$repository/.vos-engine}"

if [[ ! -x "$platform/ci/stage-the-testing-tools.sh" ]]; then
  echo "No platform checkout at $platform — pass one as the first argument." >&2
  exit 2
fi

rm -rf "$destination"
mkdir -p "$destination"

dotnet publish "$platform/vos.Mycelium/vos.Mycelium.csproj" --nologo --verbosity quiet \
  --output "$destination/vos.Mycelium"
"$platform/ci/stage-the-testing-tools.sh" "$destination/tools/testing"

echo "Staged $("$platform/ci/declared-version.sh" 2>/dev/null || echo "the engine") in $destination"
