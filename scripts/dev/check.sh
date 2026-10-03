#!/usr/bin/env bash
# Run CI's checks locally, unattended. Prints one line per step and keeps each
# step's full output in .dev/logs/<step>.log; a failing step also prints the
# failure lines from its log. Exits non-zero if any step failed.
#
#   scripts/dev/check.sh                 every stage (~2 min warm)
#   scripts/dev/check.sh quick           .NET tests + npm package tests (~30 s)
#   scripts/dev/check.sh node rendering  any of: dotnet node docs rendering
#   scripts/dev/check.sh setup           just bring the worktree up to date
#
# Setup runs first every time and is cheap when nothing changed: it installs
# missing prerequisites (the .NET 8 runtime via dotnetup, Playwright browsers),
# runs `npm ci` when package-lock.json changed, and builds the CLI and packages.
set -uo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/env.sh"
cd "$GLOSHARP_REPO"

LOGS="$GLOSHARP_REPO/.dev/logs"
mkdir -p "$LOGS"
failed=()

step() { # step <name> <command...>
  local name="$1"; shift
  local log="$LOGS/$name.log" start=$SECONDS
  if "$@" > "$log" 2>&1; then
    printf '  ok    %-22s %3ss\n' "$name" $((SECONDS - start))
  else
    printf '  FAIL  %-22s %3ss  %s\n' "$name" $((SECONDS - start)) "$log"
    { grep -E 'FAIL|✘|×|Error:|error [A-Z]+[0-9]+:|[Ff]ailed' "$log" || tail -n 20 "$log"; } | head -n 30 | sed 's/^/        | /'
    failed+=("$name")
    return 1
  fi
}

ensure_dotnet8() {
  dotnet --list-runtimes | grep -q '^Microsoft.NETCore.App 8\.' && return
  command -v dotnetup > /dev/null || { echo "Install the .NET 8 runtime (the CLI and tests target net8.0)."; return 1; }
  dotnetup runtime install 8.0 --no-progress
}

ensure_npm() {
  local stamp="$GLOSHARP_REPO/.dev/npm-ci.stamp" want
  want="$(sha256sum package-lock.json | cut -d' ' -f1)"
  [ -d node_modules ] && [ "$(cat "$stamp" 2> /dev/null)" = "$want" ] && return
  npm ci --no-audit --no-fund && echo "$want" > "$stamp"
}

build_dotnet() {
  dotnet restore &&
    dotnet restore tests/GloSharp.Tests/fixtures/sample-project/SampleProject.csproj &&
    dotnet build --no-restore -c Release
}

ensure_browsers() { (cd tests/rendering && npx playwright install chromium firefox); }

free_port() { python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])'; }

stage_dotnet() {
  step dotnet-test dotnet test --no-build -c Release
}

stage_packages() {
  step npm-test npm test
}

stage_node() {
  step version-check node scripts/version.mjs check
  stage_packages
  step check-pack node .github/scripts/check-pack.mjs
  step gitbook-typecheck npm run typecheck:integration -w @glosharp/gitbook
  step gitbook-manifest env GLOSHARP_GITBOOK_ORG=ci npm run gitbook:check -w @glosharp/gitbook
  step e2e npm test -w tests/e2e
  step fixtures-check npm run fixtures:check -w tests/rendering
}

stage_docs() {
  step verify-docs npm run verify:docs
  step build-website npm run build -w website
  step build-astro-blog npm run build -w examples/astro-blog
  step build-ec-example npm run build -w examples/expressive-code
  step build-docusaurus npm run build -w examples/docusaurus-docs
  step render-standalone npm run render -w examples/standalone
  # Integrations fall back to plain code when the CLI fails, so check for hovers.
  step assert-hovers node .github/scripts/assert-rendered.mjs website/dist examples/astro-blog/dist \
    examples/expressive-code/dist examples/docusaurus-docs/build examples/standalone/output.html
}

stage_rendering() {
  step playwright-browsers ensure_browsers || return
  # A free port per run: the suite reuses any server already on its port, which
  # could be another worktree's gallery.
  step rendering env GALLERY_PORT="$(free_port)" npm test -w tests/rendering
}

stages=("$@")
[ ${#stages[@]} -eq 0 ] && stages=(dotnet node docs rendering)
[ "${stages[*]}" = quick ] && stages=(dotnet packages)
for s in "${stages[@]}"; do
  case "$s" in
    setup | dotnet | node | docs | rendering | packages) ;;
    *) echo "unknown stage: $s (expected quick, setup, dotnet, node, docs, rendering)" >&2; exit 2 ;;
  esac
done

echo "setup"
step dotnet8-runtime ensure_dotnet8 &&
  step npm-ci ensure_npm &&
  step build-dotnet build_dotnet &&
  step build-packages npm run build ||
  { echo "setup failed; skipping checks"; exit 1; }

for s in "${stages[@]}"; do
  [ "$s" = setup ] && continue
  echo "$s"
  "stage_$s"
done

if [ ${#failed[@]} -gt 0 ]; then
  echo "FAILED: ${failed[*]}"
  exit 1
fi
echo "all checks passed"
