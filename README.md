# Locora

Laragon-like Windows local development manager built around `Microsoft.UI.Xaml` / WinUI 3.

- [Laragon parity plan](docs/locora-laragon-parity-plan.md)
- [Phase 1 scaffold](docs/phase-1-scaffold.md)
- [Project scope checklist](docs/project-scope-checklist.md)

## Current state

- Multi-project solution: `Locora.App`, `Locora.Supervisor`, `Locora.Application`, `Locora.Infrastructure`, `Locora.Domain`, `Locora.App.Contracts`
- WinUI 3 shell scaffold with Dashboard, Services, Domains & Hosts, Diagnostics, Logs, and Settings
- Shell top bar includes a Ctrl+K command palette for page navigation, common supervisor actions, and project actions
- Shell-level notifications surface action success, warning, and error toasts across all pages
- Empty service, project, diagnostic, and activity lists show recovery cards with the next safe action
- Dashboard includes a persistent first-run setup guide that can be hidden or restored from Settings
- Settings uses advanced expandable groups for portable paths, JSON config files, domain defaults, permissions, tray behavior, and diagnostics
- Accessibility pass adds screen-reader help text, contextual automation names, access keys, and live notifications across the shell
- Responsive tuning stacks dense dashboard, diagnostics, settings, services, and shell toolbar layouts on narrow windows
- Portable workspace layout under `usr/`, `www/`, `bin/`, `data/`, and `temp/`
- Named-pipe IPC skeleton between UI and supervisor
- Runtime-backed supervisor slice with config-driven managed services in `usr/config/services.json`
- Supervisor now resolves `current` aliases or matching `bin/<service>/<version>` folders to the newest installed runtime that satisfies the configured version range, and surfaces available-version hints when no match exists
- Package management now has a file-backed source registry plus install lifecycle flow that loads bundled manifests from `package-manifests/`, tracks source definitions in `usr/config/sources.json`, stores requested-version lock state in `usr/config/packages.lock.json`, stages artifacts into `usr/cache/packages`, validates cached SHA-256 checksums when manifests provide them, extracts cached archives into `bin/<package>/<version>`, refreshes `bin/<package>/current`, supports one-click install/update of active selections plus removal of installed package folders, persists `InstalledPackages` state, and surfaces catalog/cache health in Settings plus diagnostic reports
- Settings now includes a runtime version switching surface for manifest-backed PHP, Node.js, Python, and Java packages; switching an installed version refreshes the active alias immediately, while missing versions become the next package install target
- Settings now also inventories manifest-backed tool packages such as Mailpit and Composer, including provided commands, selected/install state, and whether active tool bins are ready for project terminals
- Diagnostics now validates package/service compatibility so configured services, requested versions, active package selections, and manifest executable paths stay aligned before profiles or service starts rely on them
- Stack profiles are stored in `usr/config/profiles.json`, surfaced in Settings and the command palette, persist the active profile, merge profile package selections into `packages.lock.json`, validate service references, and make Start All follow the active profile service set
- Supervisor now generates starter configs for `Nginx`, `Apache`, and `MariaDB` under `usr/config/nginx`, `usr/config/apache`, and `usr/config/mariadb`
- Supervisor now scaffolds optional `PostgreSQL` with generated `postgresql.conf`, `pg_hba.conf`, connection details, and first-start cluster initialization via `initdb`
- Supervisor now scaffolds optional `Redis`, `Memcached`, and `Mailpit` services, generates `redis.conf`, and writes connection details under `usr/config/memcached` and `usr/config/mailpit`
- Services page supports per-service start/stop actions through the supervisor
- Services page can launch detected desktop database admin tools for MariaDB and PostgreSQL, copy local connection snippets, and open generated connection detail files or config folders
- Supervisor discovers projects under `www/`, generates local URLs, supports per-project `.locora.json` domain overrides plus dashboard description/tags, skips colliding hostnames safely, writes Nginx and Apache vhosts, and produces a safe hosts-file preview
- Dashboard project cards can be pinned to the top of the list and can open the local URL, folder, terminal, and preferred editor directly
- Dashboard project cards now expose a right-click context menu plus command-palette actions for opening the site/folder, revealing the project folder in File Explorer, copying the folder path, opening a terminal/editor, and pinning the project
- Settings can generate current-user Windows Explorer context menu registration files under `usr/shell`, with install/uninstall `.reg` files for Locora folder/background menus and a command relay that forwards Explorer invocations to the running app instance when possible
- Preferred editor launches now choose the best project-aware target (`.code-workspace`, `.slnx` / `.sln`, single `*proj`, then folder), try common local editors such as VS Code, Cursor, Windsurf, VSCodium, Visual Studio, and JetBrains IDEs, and support custom `PreferredEditor` command templates in `usr/config/appsettings.json`
- Project terminal launches now inject `LOCORA_*` project metadata, `APP_URL`, family-specific runtime variables such as `PHP_BINARY`, `PYTHONHOME`, and `JAVA_HOME` when available, prepend active runtime paths such as `bin/php/current`, `bin/nodejs/current`, `bin/python/current`, or `bin/java/current`, and add `usr/aliases` plus `LOCORA_ALIASES_ROOT` / `LOCORA_TERMINAL_COMMANDS_FILE` for custom shell shims and presets
- Installed tool packages with active or extracted binaries are also prepended to project terminal `PATH`, with `LOCORA_TOOL_*` environment variables exposing their roots, versions, and command paths
- Terminal page now starts built-in ConPTY-backed sessions on Windows, falls back to redirected shell processes when ConPTY is unavailable, opens project-scoped sessions from project cards with the same injected environment, manages multiple sessions through a closable tab strip, and loads quick commands from `usr/config/terminal-commands.json` with one-click access to the aliases folder
- Domains & Hosts page can generate a local CA, issue per-project HTTPS certificates, trust or untrust the CurrentUser root store, and report trust state
- Domains & Hosts and Diagnostics can repair local SSL by recreating missing, expired, invalid, or stale project certificates and regenerating HTTPS artifacts
- Domains & Hosts page can apply the generated hosts preview as a managed block and roll back from backups
- Domains & Hosts page can edit the generated domain suffix and default scheme in `usr/config/projects.json`, then regenerate hosts and vhost artifacts in one step
- Domains & Hosts and Diagnostics expose a dedicated repair action for regenerating hosts previews plus Nginx and Apache vhost artifacts
- Domains & Hosts page can request a UAC restart for `Locora.Supervisor` when hosts writes need administrator rights
- Settings and Domains & Hosts now surface permission/elevation diagnostics for workspace writes, hosts automation, UAC restart availability, and CurrentUser SSL trust access
- Supervisor now diagnoses configured service ports, duplicate service port assignments, and external TCP listeners with PID/process hints on Windows
- Services and Domains & Hosts now expose one-click repair actions that regenerate common runtime configs, vhosts, hosts previews, and per-service artifacts before re-validating Nginx
- Settings can copy or export a Markdown diagnostic report covering services, health issues, validation results, port diagnostics, permissions, SSL certificate diagnostics, projects, and key paths
- Diagnostics page centralizes health issues, config validation, port conflicts, permission checks, local SSL state, quick repair actions, and report export
- Locora now runs with a real Windows tray icon, hides to the notification area on close, and exposes tray quick actions for reopen, diagnostics, refresh, start all, stop all, and exit
- Domains & Hosts page can validate the generated Nginx config, and supervisor blocks Nginx startup when validation fails
- Managed services now restart after unexpected exits with backoff, capped attempts, and crash-loop suspension surfaced in health issues
- Supervisor snapshot now reports admin/elevated state and process id, and supervisor startup uses a per-root mutex to avoid pipe collisions
- Duplicate app/supervisor launches now show a user-facing warning instead of silently exiting

## Open on Windows

1. Install Visual Studio 2022 with WinUI 3 / Windows App SDK support.
2. Open `Locora.sln`.
3. Start `Locora.Supervisor`, then `Locora.App`.
4. Set `LOCORA_ROOT` in your debug profile if you want the portable root to point to this repo folder while debugging.
