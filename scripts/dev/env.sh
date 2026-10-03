# Source this (`source scripts/dev/env.sh`) to run the repo's tools by hand the way
# scripts/dev/check.sh runs them. Bash only.

GLOSHARP_REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export GLOSHARP_REPO

# `glosharp` on PATH runs this worktree's Release build of the CLI (built by
# check.sh, or `npm run cli:build`); the integrations and examples find it there.
# GLOSHARP_EXECUTABLE stays unset: it overrides the mocked executable in the
# @glosharp/core unit tests and fails them.
mkdir -p "$GLOSHARP_REPO/.dev/bin"
if [ ! -x "$GLOSHARP_REPO/.dev/bin/glosharp" ]; then
  cat > "$GLOSHARP_REPO/.dev/bin/glosharp" <<EOF
#!/usr/bin/env bash
exec dotnet "$GLOSHARP_REPO/src/GloSharp.Cli/bin/Release/net8.0/GloSharp.Cli.dll" "\$@"
EOF
  chmod +x "$GLOSHARP_REPO/.dev/bin/glosharp"
fi
case ":$PATH:" in
  *":$GLOSHARP_REPO/.dev/bin:"*) ;;
  *) export PATH="$GLOSHARP_REPO/.dev/bin:$PATH" ;;
esac

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# Plain logs: check.sh greps them for failures.
export NO_COLOR=1 FORCE_COLOR=0

# Astro 7 detects coding agents and turns `astro dev` into a detached background
# process that resolves astro from <site>/node_modules, which npm workspaces
# hoist away, so it dies on start. This makes it run in the foreground.
export ASTRO_DEV_BACKGROUND=1
# Let Vite dev servers (Astro included) answer requests proxied through
# scripts/dev/preview.sh, which arrive with the tailnet host name.
export __VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS=.ts.net
