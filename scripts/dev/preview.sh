#!/usr/bin/env bash
# Publish local output on the tailnet so the user can open it from a chat reply.
# Each preview gets its own HTTPS port (9400-9499) on this machine's tailnet name,
# served at the root so absolute asset paths (/_astro/...) keep working. Only
# ports in that range are touched; other `tailscale serve` config is left alone.
#
#   scripts/dev/preview.sh <dir>        serve a build output directory
#   scripts/dev/preview.sh <file>       serve the file's directory, print the file's URL
#   scripts/dev/preview.sh <port>       proxy a local server (http://127.0.0.1:<port>)
#   scripts/dev/preview.sh ls           list previews
#   scripts/dev/preview.sh stop <port>|all
#
# Prints the URL and checks that it answers. Previews persist across reboots
# until stopped. Vite/Astro dev servers need scripts/dev/env.sh sourced so they
# accept the tailnet host name.
set -euo pipefail

FIRST_PORT=9400 LAST_PORT=9499

ts() { # serving paths needs root; operators can only proxy
  tailscale "$@" 2> /dev/null || sudo -n tailscale "$@"
}

host() { tailscale status --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))'; }

# "<port> <target>" for every preview in our range
previews() {
  tailscale serve status --json | python3 -c '
import json, sys
cfg = json.load(sys.stdin)
for hostport, web in sorted((cfg.get("Web") or {}).items()):
    port = int(hostport.rsplit(":", 1)[1])
    if '"$FIRST_PORT"' <= port <= '"$LAST_PORT"':
        h = web["Handlers"].get("/", {})
        print(port, h.get("Path") or h.get("Proxy") or "?")'
}

stop() {
  ts serve --https="$1" off > /dev/null
  echo "stopped $1"
}

case "${1:-}" in
  "" | -h | --help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  ls)
    h="$(host)"
    previews | while read -r port target; do echo "https://$h:$port/  ->  $target"; done
    exit 0 ;;
  stop)
    if [ "${2:-}" = all ]; then
      previews | while read -r port _; do stop "$port"; done
    else
      stop "${2:?usage: preview.sh stop <port>|all}"
    fi
    exit 0 ;;
esac

arg="$1" page=""
if [[ "$arg" =~ ^[0-9]+$ ]]; then
  target="http://127.0.0.1:$arg"
elif [ -d "$arg" ]; then
  target="$(cd "$arg" && pwd)"
elif [ -f "$arg" ]; then
  target="$(cd "$(dirname "$arg")" && pwd)"
  page="$(basename "$arg")"
else
  echo "not a directory, file or port: $arg" >&2; exit 2
fi

# Reuse the port already serving this target; otherwise take the first free one.
port="$(previews | awk -v t="$target" '$2 == t { print $1; exit }')"
if [ -z "$port" ]; then
  used=" $(previews | cut -d' ' -f1 | tr '\n' ' ')"
  for p in $(seq $FIRST_PORT $LAST_PORT); do
    [[ "$used" == *" $p "* ]] || { port=$p; break; }
  done
  [ -n "$port" ] || { echo "all preview ports in use; run: $0 stop all" >&2; exit 1; }
  ts serve --bg --https="$port" "$target" > /dev/null
fi

url="https://$(host):$port/$page"
code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 "$url" || true)"
echo "$url"
[ "$code" = 200 ] || { echo "warning: $url answered HTTP $code" >&2; exit 1; }
