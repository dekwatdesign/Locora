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
- Generic Host bootstrapping in both the app and supervisor
- JSON config from `usr/config/appsettings.json`
- JSON-lines logging under `usr/logs/*.log.jsonl`
- Named-pipe IPC contract between shell and supervisor
- Config-driven managed services from `usr/config/services.json`
- Optional Apache service definition with generated `httpd.conf`
- Optional PostgreSQL service definition with generated `postgresql.conf` / `pg_hba.conf` and first-start cluster initialization
- Optional Redis, Memcached, and Mailpit service definitions with generated Redis config plus Memcached and Mailpit connection details
- Runtime probing for executable presence, port responsiveness, and tracked process state
- Port conflict diagnostics for duplicate configured ports and external TCP listeners, including PID/process hints on Windows
- Stdout/stderr log streaming into `usr/logs/<service>.stdout.log` and `usr/logs/<service>.stderr.log`
- Project discovery from `www/`
- Framework/runtime hints for Laravel, WordPress, Symfony, Node/Next.js, Python, PHP, and static projects
- Project quick actions for URL, folder, terminal, and editor
- Mailpit inbox utility card with open-inbox, copy-SMTP-config, and connection-details quick actions
- Nginx vhost generation under `usr/config/nginx/vhosts`
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
- Terminal hosting
- Package management
- Import from existing Laragon installs

## Open on Windows

1. Install Visual Studio 2022 or newer with:
   - .NET desktop development
   - Windows App SDK / WinUI 3 support
2. Open [Locora.sln](/mnt/d/APP/Locora/Locora.sln)
3. Set `Locora.Supervisor` and `Locora.App` as startup projects, or launch `Locora.Supervisor` first and `Locora.App` second.
4. If you want the portable root to resolve to the repo root while debugging, set `LOCORA_ROOT=/absolute/path/to/Locora` in the Windows debug profile.

## Suggested next implementation step

Build on the new runtime-backed supervisor slice:

- Add Apache vhost template generation
