# Locora Project Scope Checklist

This checklist tracks product and engineering scope for building Locora into a Laragon-parity Windows local development manager.

Legend:

- `[x]` done in the current scaffold
- `[~]` partially done / scaffolded
- `[ ]` not started

## 1. Foundation

- [x] Multi-project solution structure
- [x] Shared package/version management
- [x] Layered architecture: App / Application / Domain / Infrastructure / Supervisor / Contracts
- [x] Portable directory layout bootstrap: `usr`, `www`, `bin`, `data`, `temp`
- [x] JSON config bootstrap under `usr/config`
- [x] Structured JSON-lines logging
- [x] Named-pipe IPC contract between UI and supervisor
- [x] Tray integration seam
- [x] Real tray icon and tray quick actions
- [x] Single-instance behavior
- [ ] App update mechanism
- [ ] Installer strategy or portable distribution automation

## 2. UI Shell and UX

- [x] WinUI 3 shell using `Microsoft.UI.Xaml`
- [x] Main navigation shell
- [x] Dashboard screen
- [x] Services screen
- [x] Logs screen
- [x] Settings screen
- [x] Diagnostics center screen
- [x] MVVM view model wiring
- [~] UX-oriented summary cards and warning surfaces
- [ ] First-run onboarding flow
- [ ] Search / command palette
- [ ] Global notifications / toasts
- [ ] Empty-state recovery actions
- [ ] Advanced settings UX
- [ ] Accessibility pass
- [ ] Responsive tuning for narrow window sizes

## 3. Supervisor and Runtime Control

- [x] Supervisor process project
- [~] Supervisor state store
- [x] Start all / stop all shell actions wired through IPC
- [x] Start / stop individual services through IPC and UI
- [x] Supervisor lifecycle orchestration shape
- [x] Real process spawning
- [x] Real process stop / kill logic
- [x] PID tracking
- [~] Health checks
- [x] Restart policies
- [x] Port probing / port conflict detection
- [x] Stdout / stderr capture per service
- [x] Service-specific adapters
- [x] Nginx config validation before startup
- [x] Failure recovery / repair commands

## 4. Web Servers and Databases

- [x] Nginx real adapter
- [x] Apache real adapter
- [x] MariaDB / MySQL real adapter
- [x] PostgreSQL adapter
- [x] Redis adapter
- [x] Memcached adapter
- [x] Mailpit / mail catcher adapter
- [~] Per-service config generation
- [ ] Version-aware service registration
- [x] Auto-start selection persistence

## 5. Project Discovery

- [x] Projects shown in UI from supervisor snapshot
- [x] Real project root scanning
- [x] Framework detection from project markers
- [x] Runtime inference per project
- [x] Auto local URL generation
- [ ] Favorite / pinned projects
- [ ] Project tags / metadata
- [x] Open in editor / terminal / browser / explorer actions

## 6. Domains and VHosts

- [ ] Apache vhost template generation
- [x] Nginx vhost template generation
- [~] Automatic local domain mapping
- [~] Hosts file preview generation
- [~] Privileged hosts file editing
- [ ] Collision detection for domains
- [ ] Custom TLD support
- [ ] Per-project domain overrides
- [ ] Repair hosts / vhost actions

## 7. Local SSL

- [x] Local CA management
- [x] Certificate generation per domain
- [x] Windows trust-store integration
- [~] Certificate renewal / regeneration
- [x] SSL status display in UI
- [x] SSL repair flow

## 8. Package and Runtime Management

- [ ] Package manifest schema
- [ ] Package source registry
- [ ] Download manager
- [ ] Checksum validation
- [ ] Archive extraction
- [ ] Install / remove / update packages
- [ ] PHP multi-version management
- [ ] Node.js multi-version management
- [ ] Python runtime support
- [ ] Java runtime support
- [ ] Tool package support
- [ ] Runtime switching UI
- [ ] Compatibility validation between packages and profiles

## 9. Developer Utilities

- [ ] Built-in ConPTY terminal
- [ ] Tabbed terminal sessions
- [ ] Project-scoped shell environment injection
- [ ] Aliases and custom commands
- [ ] Open database admin tools
- [x] Mailpit inbox integration
- [ ] Editor integration
- [ ] Explorer/context menu integration

## 10. Profiles and Automation

- [ ] Stack profiles
- [ ] Save / load active environment
- [ ] Per-project overrides
- [ ] Import / export profiles
- [ ] Service presets
- [ ] Custom tools menu

## 11. Diagnostics and Repair

- [x] Logs surface in the shell
- [x] Health issue area in dashboard
- [x] Diagnostics center
- [x] Port conflict diagnostics
- [x] Permission / elevation diagnostics
- [~] Missing runtime diagnostics
- [~] Broken config diagnostics
- [x] One-click repair actions
- [x] Copyable diagnostic report

## 12. Sharing and External Access

- [ ] Local tunnel integration
- [ ] Share URL lifecycle
- [ ] Consent and warning UX
- [ ] Firewall / network guidance

## 13. Import, Backup, and Migration

- [ ] Import from Laragon layout/config
- [ ] Config backup
- [ ] Config restore
- [ ] Runtime backup guidance
- [ ] Portable relocation validation

## 14. Security and Privileged Operations

- [~] Elevation broker strategy
- [~] Safe confirmation UX for privileged actions
- [x] Rollback for `hosts` edits
- [x] Rollback for certificate trust operations
- [ ] PATH/environment variable change management

## 15. Testing and Release Readiness

- [ ] Unit test projects
- [ ] Integration test projects
- [ ] E2E test projects
- [ ] Snapshot tests for generated configs
- [ ] Windows 10 validation
- [ ] Windows 11 validation
- [ ] Paths with spaces validation
- [ ] Portable move-to-new-drive validation
- [ ] Build pipeline / CI
- [ ] Release packaging

## Suggested Next Slice

If building in the highest-value order, do these next:

- [x] Define `IManagedService` abstraction
- [x] Implement real `NginxServiceAdapter`
- [x] Implement real `MariaDbServiceAdapter`
- [x] Replace mock `SupervisorStateStore` with runtime-backed state
- [~] Add port probing and process health checks
- [x] Stream real service logs into `usr/logs`
- [x] Persist service auto-start settings

## New Highest-Value Slice

- [x] Generate service-specific configs for Nginx and MariaDB
- [x] Add restart policies and better crash recovery
- [x] Implement per-service start/stop commands from the UI
- [x] Detect projects from `www/` and suggest domain mappings
- [~] Generate hosts/vhost entries
- [x] Wire local SSL management

## Completed Recent Slice

- [x] Add Mailpit inbox integration
- [x] Add PostgreSQL adapter
- [x] Add Redis and Mailpit adapters
- [x] Add Memcached adapter
- [x] Add Apache real adapter
- [x] Add real tray icon and tray quick actions
- [x] Add privileged hosts-file apply flow with preview and rollback
- [x] Add an elevation broker / restart-supervisor-as-admin flow
- [x] Add supervisor admin-state detection and surface it in the UI
- [x] Add single-instance supervisor behavior to avoid pipe collisions after elevation
- [x] Add user-facing duplicate-instance messages for app and supervisor
- [x] Add Nginx config validation before service startup/restart
- [x] Add per-project actions: open URL, folder, terminal, and editor
- [x] Add local SSL certificate generation and trust-state display
- [x] Add rollback for certificate trust operations
- [x] Add restart policies and better crash recovery
- [x] Add permission and elevation diagnostics
- [x] Add one-click repair actions for common runtime/config failures
- [x] Add copyable/exportable Markdown diagnostic reports
- [x] Add a dedicated diagnostics center view
- [x] Add deeper port conflict diagnostics and collision detection
- [x] Add SSL repair flow for expired or missing project certificates

## Next Highest-Value Slice

- [ ] Add Apache vhost template generation
