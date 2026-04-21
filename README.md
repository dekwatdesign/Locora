# Locora

Laragon-like Windows local development manager built around `Microsoft.UI.Xaml` / WinUI 3.

- [Laragon parity plan](docs/locora-laragon-parity-plan.md)
- [Phase 1 scaffold](docs/phase-1-scaffold.md)
- [Project scope checklist](docs/project-scope-checklist.md)

## Current state

- Multi-project solution: `Locora.App`, `Locora.Supervisor`, `Locora.Application`, `Locora.Infrastructure`, `Locora.Domain`, `Locora.App.Contracts`
- WinUI 3 shell scaffold with Dashboard, Services, Domains & Hosts, Diagnostics, Logs, and Settings
- Portable workspace layout under `usr/`, `www/`, `bin/`, `data/`, and `temp/`
- Named-pipe IPC skeleton between UI and supervisor
- Runtime-backed supervisor slice with config-driven managed services in `usr/config/services.json`
- Supervisor now generates starter configs for `Nginx` and `MariaDB` under `usr/config/nginx` and `usr/config/mariadb`
- Supervisor now scaffolds optional `PostgreSQL` with generated `postgresql.conf`, `pg_hba.conf`, connection details, and first-start cluster initialization via `initdb`
- Supervisor now scaffolds optional `Redis` and `Mailpit` services, generates `redis.conf`, and writes Mailpit SMTP/UI connection details under `usr/config/mailpit`
- Services page supports per-service start/stop actions through the supervisor
- Supervisor discovers projects under `www/`, generates local URLs, writes Nginx vhosts, and produces a safe hosts-file preview
- Dashboard project cards can open the local URL, folder, terminal, and preferred editor directly
- Domains & Hosts page can generate a local CA, issue per-project HTTPS certificates, trust or untrust the CurrentUser root store, and report trust state
- Domains & Hosts and Diagnostics can repair local SSL by recreating missing, expired, invalid, or stale project certificates and regenerating HTTPS artifacts
- Domains & Hosts page can apply the generated hosts preview as a managed block and roll back from backups
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
