# Locora Phase 1 Scaffold

This scaffold turns the repo into a real multi-project Windows desktop solution for the first delivery slice:

- `Locora.App`: WinUI 3 shell using `Microsoft.UI.Xaml`
- `Locora.Supervisor`: background process exposing runtime-backed supervisor state through named pipes
- `Locora.Application`: orchestration layer for shell actions and snapshot loading
- `Locora.Infrastructure`: config, portable paths, JSON-line logging, bootstrap, IPC client
- `Locora.Domain`: core models
- `Locora.App.Contracts`: DTOs shared between the app and supervisor

## What works in this scaffold

- Portable folder layout rooted around `usr/`, `www/`, `bin/`, `data/`, and `temp/`
- WinUI shell with Dashboard, Services, Domains & Hosts, Diagnostics, Logs, and Settings
- Shell command palette with Ctrl+K search across pages, repair/start/stop actions, and discovered projects
- Shell-level action notifications using a global InfoBar toast surface
- Empty-state recovery cards across service, project, diagnostic, and activity lists
- Persistent first-run onboarding guide with setup progress and recovery actions
- Advanced Settings page with expandable groups for paths, JSON config, domain defaults, permissions, tray behavior, and diagnostic reports
- Accessibility pass with contextual automation names, screen-reader help text, access keys, and live shell notifications
- Responsive shell/page tuning that stacks dense Dashboard, Diagnostics, Settings, Services, and toolbar layouts on narrow windows
- Generic Host bootstrapping in both the app and supervisor
- JSON config from `usr/config/appsettings.json`
- JSON-lines logging under `usr/logs/*.log.jsonl`
- Named-pipe IPC contract between shell and supervisor
- Config-driven managed services from `usr/config/services.json`
- Version-aware service resolution that expands `current` aliases or matching `bin/<service>/<version>` folders to the newest installed runtime satisfying the configured version range
- File-backed package source registry with bundled manifest metadata in `package-manifests/`, source definitions in `usr/config/sources.json`, requested-version lock state in `usr/config/packages.lock.json`, package artifact caching under `usr/cache/packages`, SHA-256 validation for cached artifacts when manifests provide hashes, archive extraction into `bin/<package>/<version>`, best-effort refresh of `bin/<package>/current`, one-click install/update for active selections, removal of installed package folders, runtime version switching for PHP, Node.js, Python, and Java packages, package/service compatibility validation, and UI/reporting hooks for source health plus download/cache/runtime status
- Tool package inventory for manifest-backed CLIs such as Mailpit and Composer, including provided commands, selected/install state, and diagnostic report coverage
- Optional Apache service definition with generated `httpd.conf` and per-project vhosts
- Optional PostgreSQL service definition with generated `postgresql.conf` / `pg_hba.conf` and first-start cluster initialization
- Optional Redis, Memcached, and Mailpit service definitions with generated Redis config plus Memcached and Mailpit connection details
- Runtime probing for executable presence, port responsiveness, and tracked process state
- Port conflict diagnostics for duplicate configured ports and external TCP listeners, including PID/process hints on Windows
- Stdout/stderr log streaming into `usr/logs/<service>.stdout.log` and `usr/logs/<service>.stderr.log`
- Project discovery from `www/`
- Framework/runtime hints for Laravel, WordPress, Symfony, Node/Next.js, Python, PHP, Java, and static projects
- Project quick actions for URL, folder, terminal, and editor
- Project card right-click context menus plus command-palette actions for opening, revealing in File Explorer, copying folder paths, terminal/editor launch, and pinning
- Windows Explorer context menu registration files generated under `usr/shell`, including install/uninstall `.reg` files for current-user folder/background menus and a shell activation relay into the running app instance
- Preferred editor integration that chooses `.code-workspace`, `.slnx` / `.sln`, or a single `*proj` before falling back to the folder, tries common local editors such as VS Code, Cursor, Windsurf, VSCodium, Visual Studio, and JetBrains IDEs, and accepts custom `PreferredEditor` command templates
- Project terminal launches with per-project `LOCORA_*` environment variables plus active runtime PATH injection for PHP, Node.js, Python, and Java selections
- Project terminal launches also prepend installed tool package bins and expose `LOCORA_TOOL_*` environment variables for active tools
- Built-in Terminal page starts ConPTY-backed sessions on Windows, falls back to redirected shell processes when ConPTY is unavailable, opens project-scoped sessions from project cards, manages multiple sessions through a closable tab strip, loads preset commands from `usr/config/terminal-commands.json`, and exposes the portable `usr/aliases` folder on terminal `PATH`
- Persisted pinned projects in the dashboard via `usr/config/project-pins.json`
- Per-project `.locora.json` metadata for generated domains, descriptions, and tags
- Mailpit inbox utility card with open-inbox, copy-SMTP-config, and connection-details quick actions
- Services page launchers for MariaDB/PostgreSQL database admin tools plus copy/open connection detail actions
- Nginx vhost generation under `usr/config/nginx/vhosts`
- Apache vhost generation under `usr/config/apache/vhosts`
- Editable generated domain defaults (`DomainSuffix` / `DefaultScheme`) from the Domains & Hosts page, persisted to `usr/config/projects.json`
- Local SSL generation with a Locora CA, per-project cert material, trust rollback, and HTTPS vhosts when certs exist
- Local SSL repair for missing, expired, invalid, or stale project certificates, with per-project certificate diagnostics
- Safe hosts-file preview generation at `usr/config/hosts.locora.generated`
- Hosts-file apply/rollback flow using a managed Locora block and backups under `usr/config/hosts.backups`
- Restart-supervisor-as-admin command for UAC-protected hosts operations
- Supervisor admin-state reporting and per-root single-instance mutex
- User-facing duplicate-instance warnings for both app and supervisor
- Permission and elevation diagnostics for portable workspace writes, hosts automation, UAC restart availability, and CurrentUser SSL trust access
- One-click repair actions for common runtime/config failures and stale generated artifacts
- Copyable and exportable Markdown diagnostic reports from the Settings page
- Dedicated Diagnostics page for health issues, config validation, port conflicts, permissions, SSL state, recovery actions, and report export
- Real Windows tray icon with close-to-tray behavior and quick actions for reopen, diagnostics, refresh, start all, stop all, and exit
- Nginx config validation with startup blocking when validation fails
- Crash recovery with restart backoff, capped retry attempts, and suspension after repeated failures

## What is intentionally still stubbed

- Production-ready repair for generated service configs
- Additional adapters for other tools
- Import from existing Laragon installs

## Open on Windows

1. Install Visual Studio 2022 or newer with:
   - .NET desktop development
   - Windows App SDK / WinUI 3 support
2. Open [Locora.sln](/mnt/d/APP/Locora/Locora.sln)
3. Set `Locora.Supervisor` and `Locora.App` as startup projects, or launch `Locora.Supervisor` first and `Locora.App` second.
4. If you want the portable root to resolve to the repo root while debugging, set `LOCORA_ROOT=/absolute/path/to/Locora` in the Windows debug profile.

## Suggested next implementation step

Build on the new stack profile slice:

- Save / load active environment
- Per-project overrides
- Import / export profiles
