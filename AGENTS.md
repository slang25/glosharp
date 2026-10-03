## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues on slang25/glosharp, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Validating changes

`scripts/dev/check.sh` runs CI's checks unattended (~2 min); `scripts/dev/check.sh quick` is the inner loop (~30 s). Work is done when it ends with `all checks passed`. It prepares the worktree itself (.NET 8 runtime, `npm ci`, CLI build, Playwright browsers), prints only the failing lines, and keeps full logs in `.dev/logs/`.

To run a tool by hand, `source scripts/dev/env.sh` first: it puts this worktree's CLI on PATH as `glosharp` and keeps `astro dev` in the foreground (Astro 7 detaches it under agents, then crashes in npm workspaces).

## Showing the user output

When the user should see a page (a UI change, a rendered example, the rendering gallery in `tests/rendering/gallery-dist`), build it, run `scripts/dev/preview.sh <dir|file|port>`, and put the printed tailnet URL in your reply. The script checks the URL loads. `scripts/dev/preview.sh ls` lists previews; `stop <port>` removes one.
