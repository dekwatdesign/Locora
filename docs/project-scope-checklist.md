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
- [x] App update mechanism
- [x] Installer strategy or portable distribution automation

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
- [x] First-run onboarding flow
- [x] Search / command palette
- [x] Global notifications / toasts
- [x] Empty-state recovery actions
- [x] Advanced settings UX
- [x] Accessibility pass
- [x] Responsive tuning for narrow window sizes

## 3. Supervisor and Runtime Control

- [x] Supervisor process project
- [x] Supervisor state store
- [x] Start all / stop all shell actions wired through IPC
- [x] Start / stop individual services through IPC and UI
- [x] Supervisor lifecycle orchestration shape
- [x] Real process spawning
- [x] Real process stop / kill logic
- [x] PID tracking
- [x] Health checks
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
- [x] Per-service config generation
- [x] Version-aware service registration
- [x] Auto-start selection persistence

## 5. Project Discovery

- [x] Projects shown in UI from supervisor snapshot
- [x] Real project root scanning
- [x] Framework detection from project markers
- [x] Runtime inference per project
- [x] Auto local URL generation
- [x] Favorite / pinned projects
- [x] Project tags / metadata
- [x] Open in editor / terminal / browser / explorer actions

## 6. Domains and VHosts

- [x] Apache vhost template generation
- [x] Nginx vhost template generation
- [x] Automatic local domain mapping
- [x] Hosts file preview generation
- [x] Privileged hosts file editing
- [x] Collision detection for domains
- [x] Custom TLD support
- [x] Per-project domain overrides
- [x] Repair hosts / vhost actions

## 7. Local SSL

- [x] Local CA management
- [x] Certificate generation per domain
- [x] Windows trust-store integration
- [x] Certificate renewal / regeneration
- [x] SSL status display in UI
- [x] SSL repair flow

## 8. Package and Runtime Management

- [x] Package manifest schema
- [x] Package source registry
- [x] Download manager
- [x] Checksum validation
- [x] Archive extraction
- [x] Install / remove / update packages
- [x] PHP multi-version management
- [x] Node.js multi-version management
- [x] Python runtime support
- [x] Java runtime support
- [x] Tool package support
- [x] Runtime switching UI
- [x] Compatibility validation between packages and profiles

## 9. Developer Utilities

- [x] Built-in ConPTY terminal
- [x] Tabbed terminal sessions
- [x] Project-scoped shell environment injection
- [x] Aliases and custom commands
- [x] Open database admin tools
- [x] Mailpit inbox integration
- [x] Editor integration
- [x] Explorer/context menu integration

## 10. Profiles and Automation

- [x] Stack profiles
- [x] Save / load active environment
- [x] Per-project overrides
- [x] Import / export profiles
- [x] Service presets
- [x] Custom tools menu

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

- [x] Local tunnel integration
- [x] Share URL lifecycle
- [x] Consent and warning UX
- [x] Firewall / network guidance

## 13. Import, Backup, and Migration

- [x] Import from Laragon layout/config
- [x] Config backup
- [x] Config restore
- [x] Runtime backup guidance
- [x] Portable relocation validation

## 14. Security and Privileged Operations

- [x] Elevation broker strategy
- [x] Safe confirmation UX for privileged actions
- [x] Rollback for `hosts` edits
- [x] Rollback for certificate trust operations
- [x] Windows shell context menu registration
- [x] PATH/environment variable change management

## 15. Testing and Release Readiness

- [x] Unit test projects
- [x] Integration test projects
- [x] E2E test projects
- [x] Snapshot tests for generated configs
- [ ] Windows 10 validation
- [x] Windows 11 validation
- [x] Paths with spaces validation
- [x] Portable move-to-new-drive validation
- [x] Build pipeline / CI
- [x] Release packaging

## Suggested Next Slice

If building in the highest-value order, do these next:

- [x] Define `IManagedService` abstraction
- [x] Implement real `NginxServiceAdapter`
- [x] Implement real `MariaDbServiceAdapter`
- [x] Replace mock `SupervisorStateStore` with runtime-backed state
- [x] Add port probing and process health checks
- [x] Stream real service logs into `usr/logs`
- [x] Persist service auto-start settings

## New Highest-Value Slice

- [x] Generate service-specific configs for Nginx and MariaDB
- [x] Add restart policies and better crash recovery
- [x] Implement per-service start/stop commands from the UI
- [x] Detect projects from `www/` and suggest domain mappings
- [x] Generate hosts/vhost entries
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
- [x] Add empty-state recovery actions
- [x] Add first-run onboarding flow
- [x] Add advanced settings UX
- [x] Add accessibility pass
- [x] Add responsive tuning for narrow window sizes
- [x] Add version-aware service registration
- [x] Add package manifest schema
- [x] Add package source registry
- [x] Add install/remove/update package flow
- [x] Add runtime version inventory and switching UI for manifest-backed PHP/Node/Python selections
- [x] Add package/service compatibility validation for active selections
- [x] Add project terminal environment injection with active runtime PATH and LOCORA_* variables
- [x] Add manifest-backed Python runtime catalog plus terminal environment variables
- [x] Add manifest-backed Java runtime catalog plus terminal `JAVA_HOME` support
- [x] Add manifest-backed tool package inventory plus terminal PATH injection for active tools
- [x] Add built-in ConPTY terminal sessions with project-scoped environment injection
- [x] Add closable tabbed UX for multiple built-in terminal sessions
- [x] Add aliases and custom commands
- [x] Open database admin tools
- [x] Add editor integration with project-aware target selection and preferred editor fallbacks
- [x] Add in-app Explorer reveal/copy actions and project card context menus
- [x] Add Windows shell context menu registration files and activation relay
- [x] Add stack profiles with active profile persistence, profile-aware Start All, package selection merge, Settings selection UI, command-palette actions, and diagnostic report coverage
- [x] Add save/load active environment flow with supervisor-backed profile creation, package lock capture, Settings controls, and command-palette action
- [x] Add per-project overrides for domain, scheme, document root, runtime, framework, description, and tags through `projects.json` / `.locora.json`
- [x] Add stack profile import/export through `usr/profiles`, supervisor-backed merge/export commands, Settings controls, and command-palette actions
- [x] Add service presets with validated `services.json` groups, supervisor start/stop commands, Services-page controls, command-palette actions, and diagnostic report coverage
- [x] Add custom tools menu backed by `custom-tools.json`, Settings controls, command-palette actions, URL/file/folder/editor launch support, and terminal command tools
- [x] Add local tunnel integration backed by `local-tunnels.json`, Domains-page controls, command-palette actions, terminal launch support, and tunnel placeholder resolution
- [x] Add share URL lifecycle tracking with dedicated tunnel terminal sessions, public URL detection, copy/open/stop actions, command-palette controls, and stopped-session cleanup
- [x] Add consent and warning UX for external access with session-scoped acknowledgement, tunnel command gating, and Domains-page warning state
- [x] Add firewall and network guidance for service ports, LAN inbound rules, tunnel outbound access, data-service exposure, and diagnostic reports
- [x] Add Laragon import flow that previews www projects, merges project discovery overrides, preserves external source paths, backs up projects.json, and discovers absolute override paths
- [x] Add configuration backup archives for usr/config with timestamped zip output, manifest metadata, backup folder path, Settings controls, and command-palette actions
- [x] Add configuration restore from backup zip with path validation, pre-restore backup, zip-slip protection, Settings controls, and command-palette action
- [x] Add runtime backup guidance covering service data, runtime binaries, package cache, package lock state, restore order, Settings controls, command-palette access, and diagnostic reports
- [x] Add portable relocation validation for service, project, package, runtime, and tool paths with health issue surfacing, Settings controls, command-palette access, and diagnostic report coverage
- [x] Add CurrentUser PATH/environment change management with generated install/uninstall PowerShell scripts, pre-change backups, manifest output, Settings controls, command-palette actions, and diagnostic report coverage
- [x] Add app update mechanism with configurable release manifest checks, version comparison, cached manifest and update plan files under `usr/updates`, Settings controls, command-palette actions, and diagnostic report coverage
- [x] Add portable distribution automation with generated PowerShell packaging script, portable ZIP staging plan, checksum and release manifest outputs, Settings controls, command-palette actions, and diagnostic report coverage
- [x] Add unit test projects for Domain, Application, Infrastructure, and Supervisor with xUnit, solution registration, and initial coverage for offline snapshots, runtime selection forwarding, environment paths, JSON persistence, and port diagnostics
- [x] Add integration test project with temp-root host bootstrap coverage and generated project config/vhost/hosts-preview verification
- [x] Add E2E test project that boots the supervisor host with a unique temp root and named pipe, reaches it through `NamedPipeSupervisorClient`, maps through `WorkbenchService`, discovers the welcome project, and saves an active environment profile
- [x] Add snapshot test project for generated Nginx root config, project vhost config, and hosts preview output with temp-root normalization
- [x] Validate Windows 11 build/test readiness on Microsoft Windows 11 Pro 10.0.26200 with `Locora.App` build and solution test suite passing
- [x] Add privileged action confirmation dialogs for hosts writes, elevated supervisor restart, and local SSL trust changes
- [x] Validate host bootstrap, generated configs, snapshots, and E2E supervisor flows under roots with spaces
- [x] Validate portable move-to-new-drive readiness with an integration test that reboots Locora from a `subst`-mapped drive
- [x] Add a Windows GitHub Actions workflow plus a repo-level portable packaging script that builds, tests, zips, checksums, and emits a release manifest
- [x] Validate local portable release packaging on Windows 11 by publishing `Locora.App` and creating `artifacts/dist/Locora-0.0.0-localtest-win-x64-portable.zip`

## Next Highest-Value Slice

- [ ] Windows 10 validation
