# Locora: Laragon-Parity Product and Engineering Plan

## 1. Goal

Build `Locora`, a Windows local-development environment manager inspired by Laragon, using `Microsoft.UI.Xaml` (WinUI 3) for the desktop UI and a simpler, more guided UX.

Target:

- Match Laragon's functional surface area as closely as possible.
- Improve onboarding, discoverability, recovery, and day-to-day speed.
- Keep the app portable-first, low-friction, and suitable for Windows developers who want one-click local stacks.

Important constraint:

- "100% identical" should be treated as `functional parity`, not a verbatim clone.
- We should not copy Laragon branding, assets, proprietary binaries, or undocumented behavior blindly.
- Exact parity will require black-box validation against real Laragon installs and user workflows.

## 2. Product Principles

- Portable-first: run from a folder, avoid mandatory machine-wide install.
- One-click default: common tasks should take 1 action, advanced tasks stay available.
- Safe automation: when the app edits `hosts`, certificates, PATH, or services, it must be explicit and reversible.
- Multi-runtime support: PHP, Node.js, Python, Java, databases, and web servers can coexist across versions.
- Fast recovery: startup, stop, reset, repair, and logs must be easy to find.
- Transparent state: users should always know what is running, broken, blocked, or elevated.

## 3. Laragon Parity Scope

Baseline parity should cover these capability groups.

### 3.1 Core Environment Management

- Start all / stop all / restart all services
- Start individual services
- Auto-start selected services on launch
- Portable app root with relocatable paths
- Project root scanning
- Service health checks
- Logs per service
- Runtime status badges
- Minimize to tray and tray quick actions

### 3.2 Web Stack and Runtimes

- Apache
- Nginx
- MySQL / MariaDB
- PostgreSQL
- Redis
- Memcached
- PHP with multi-version switching
- Node.js / npm / pnpm / yarn
- Python
- Java
- Optional extras through packages

### 3.3 Developer Experience Features

- Automatic virtual hosts
- Friendly local domains
- Automatic SSL for local domains
- Built-in terminal
- Quick app creation from templates
- Quick add packages/runtimes
- Context-menu actions for projects
- Open in editor / browser / terminal / explorer
- Environment variables and PATH management
- Project aliases / custom commands

### 3.4 Productivity Utilities

- Mail catcher integration
- Database admin tools launchers
- Share/tunnel support
- Profile switching for stacks and versions
- Service presets
- Custom menu items and tools
- Backup/export/import helpers

### 3.5 Reliability and Maintenance

- Config backup and restore
- Self-diagnostics
- Port conflict detection
- Permission/elevation guidance
- Repair actions for broken config, hosts, SSL, or packages
- Upgrade flow for the app and managed packages

## 4. UX Direction

Locora should be easier than Laragon in 4 ways.

### 4.1 Guided First Run

- Detect installed tools and import likely projects automatically.
- Ask 3 to 5 setup questions only: project folder, preferred web server, preferred database, terminal shell, editor.
- Show a readiness checklist instead of a dense settings wall.

### 4.2 Clear Daily Workflow

Primary home screen should answer:

- What is running?
- Which project am I working on?
- What is broken?
- What is the next useful action?

### 4.3 Fewer Hidden Behaviors

- Before changing `hosts`, cert store, firewall, or PATH, show a concise confirmation with rollback.
- Keep all generated config and actions inspectable from the UI.
- Surface logs and repair buttons right next to failing services.

### 4.4 Progressive Complexity

- Simple mode: dashboard, projects, start/stop, terminal, logs.
- Advanced mode: custom templates, package sources, aliases, custom services, custom scripts, stack profiles.

## 5. Recommended UI with Microsoft.UI.Xaml

Use `WinUI 3` with `Microsoft.UI.Xaml` as the UI layer. The visual shell should feel native to Windows 11 while staying dense enough for power users.

### 5.1 Shell Layout

- `NavigationView` for the app's main sections
- `CommandBar` for page-level actions
- `TabView` for logs, terminals, and multi-project workflows
- `InfoBar` for warnings, port conflicts, SSL notices, and repair prompts
- `ContentDialog` for elevated operations and destructive actions
- `Expander` and `SettingsCard` style groupings for advanced settings

### 5.2 Main Screens

1. Dashboard
   - Running services
   - Favorite projects
   - Active stack/profile
   - Recent errors
   - One-click actions
2. Projects
   - Auto-discovered projects
   - Local URLs
   - Runtime bindings
   - Open actions
3. Services
   - Service list with state, version, port, config path, log access
4. Packages
   - Install/update/remove runtimes and tools
5. Domains and SSL
   - VHost mappings, hosts entries, cert state, repair tools
6. Terminal
   - Built-in tabbed terminal
7. Logs and Diagnostics
   - Structured logs, filters, health checks
8. Profiles
   - Save/load stack combinations
9. Settings
   - General, directories, editor, shell, privileges, integrations

### 5.3 UX Details Worth Doing Better Than Laragon

- Inline explanation for every privileged action
- Empty states with recovery buttons, not just documentation links
- Search-first command palette for "switch PHP 8.3", "open mail", "repair hosts", "create WordPress"
- Per-project cards showing URL, PHP version, Node version, DB, SSL, and last action
- Quick-fix panel for common issues:
  - Port 80 in use
  - Hosts file locked
  - Missing VC++ runtime
  - Service failed to boot
  - Cert generation failed

## 6. Technical Architecture

## 6.1 Platform

- OS: Windows 10/11
- App model: `WinUI 3 desktop app`, preferably `unpackaged` to support portable distribution
- Language: `C#`
- Runtime: `.NET 8` or newer LTS

## 6.2 Architecture Style

Recommended architecture:

- UI: WinUI 3 + MVVM
- Application layer: use cases / orchestration
- Domain layer: projects, services, runtimes, packages, profiles
- Infrastructure layer: filesystem, processes, Windows interop, certs, hosts, networking
- Background worker layer: long-running orchestration for service monitoring, package installs, diagnostics

Suggested patterns:

- `CommunityToolkit.Mvvm` for MVVM scaffolding
- `IHostedService` or equivalent background services for monitoring
- `Named Pipes` or local IPC when splitting the UI process from a background supervisor
- JSON-based config for portability, SQLite optional for event history and analytics

## 6.3 Runtime Model

Split the system into 2 major executables:

1. `Locora.App`
   - WinUI shell
   - settings UI
   - logs viewer
   - terminal host UI
2. `Locora.Supervisor`
   - starts/stops child services
   - monitors process health
   - manages ports, pid files, and restarts
   - performs privileged operations when approved

This split keeps the UI responsive and makes crash recovery cleaner.

## 6.4 Windows Integration

Need dedicated infrastructure for:

- `hosts` file edits
- Windows certificate store
- tray icon
- shell/context menu integration
- file associations if needed
- elevation broker / restart-as-admin
- environment variables and PATH updates
- Windows Firewall rules for optional sharing/tunneling
- ConPTY for terminal integration

## 7. Functional Module Breakdown

### 7.1 Project Discovery Module

Responsibilities:

- scan root directories like `www`
- detect project types
- infer framework and runtime needs
- generate local URLs
- attach custom metadata

Detection signals:

- `composer.json`
- `package.json`
- `requirements.txt`
- `pyproject.toml`
- `.nvmrc`
- `.node-version`
- Laravel, WordPress, Symfony, Drupal, Django, Flask, Express, Next.js markers

### 7.2 Service Supervisor Module

Responsibilities:

- spawn and stop processes
- health-check ports
- capture stdout/stderr
- maintain restart policies
- expose status to the UI

### 7.3 Package Manager Module

Responsibilities:

- install/remove runtimes and tools
- download manifests
- checksum validation
- archive extraction
- version switching
- profile compatibility validation

Package sources should be manifest-driven so we can add mirrors later.

### 7.4 VHost and Domain Module

Responsibilities:

- generate Apache/Nginx configs
- update `hosts`
- map project folder to local domain
- support custom TLDs or subdomains
- validate collisions and broken routes

### 7.5 SSL Module

Responsibilities:

- create local certificates
- trust certificates on Windows
- renew/regenerate certs
- assign certificates to vhosts
- repair trust state

Preferred approach:

- use a proven local CA flow such as `mkcert` semantics, wrapped in a guided UI

### 7.6 Terminal Module

Responsibilities:

- tabbed terminal
- project-scoped shells
- runtime-aware environment injection
- quick commands

Implementation note:

- use `ConPTY` rather than building a fake console

### 7.7 Mail Module

Responsibilities:

- embed or manage `Mailpit`/mail-catcher process
- surface inbox UI or deep-link to local web UI
- show SMTP config snippet for apps

### 7.8 Share/Tunnel Module

Responsibilities:

- expose local sites externally
- start/stop tunnels
- show URLs and expiry/state
- gate behind explicit consent and warnings

### 7.9 Profiles Module

Responsibilities:

- save stack combinations
- switch PHP/WebServer/DB presets
- project-specific overrides
- import/export profiles

### 7.10 Diagnostics and Repair Module

Responsibilities:

- detect occupied ports
- detect malformed config
- detect missing package files
- detect permission failures
- run repair scripts
- produce copyable reports

## 8. Proposed Repository Structure

```text
Locora/
  docs/
  build/
  scripts/
  assets/
  package-manifests/
  src/
    Locora.App/
    Locora.App.Contracts/
    Locora.Domain/
    Locora.Application/
    Locora.Infrastructure/
    Locora.Supervisor/
    Locora.Terminal/
    Locora.Packages/
    Locora.Diagnostics/
    Locora.Tests.Unit/
    Locora.Tests.Integration/
    Locora.Tests.E2E/
```

## 9. Proposed Portable Runtime Layout

To preserve Laragon-like ergonomics, the installed app data layout should look like this:

```text
LocoraRoot/
  locora.exe
  usr/
    config/
    profiles/
    aliases/
    templates/
    logs/
    cache/
  www/
  bin/
    php/
    nginx/
    apache/
    mysql/
    postgres/
    redis/
    node/
    python/
    tools/
  data/
  temp/
```

This keeps user-managed runtimes separate from source code and makes backup/restore predictable.

## 10. Data and Config Design

Prefer human-editable config for portability.

Suggested config files:

- `usr/config/appsettings.json`
- `usr/config/projects.json`
- `usr/config/services.json`
- `usr/config/profiles.json`
- `usr/config/packages.lock.json`
- `usr/config/sources.json`

Optional local database:

- `usr/data/locora.db`

Use the database only for event history, diagnostics, and cached indexes, not as the only source of truth.

## 11. Delivery Phases

## Phase 0: Discovery and Validation

- benchmark Laragon workflows
- list parity features and edge cases
- validate portable WinUI 3 distribution model
- decide package source strategy
- define privileged-operation policy

Deliverable:

- approved spec, parity matrix, and UX wireframes

## Phase 1: Foundation

- create WinUI 3 shell
- set up MVVM infrastructure
- implement config store
- implement supervisor process
- implement tray integration
- implement structured logging

Deliverable:

- app launches, persists settings, manages tray, talks to supervisor

## Phase 2: Service Management

- add Apache/Nginx/MySQL/Redis lifecycle management
- add health checks, logs, ports, restart actions
- add installable runtime definitions

Deliverable:

- service control panel with real process supervision

## Phase 3: Projects, Domains, SSL

- add project scanner
- add project cards and actions
- generate vhosts
- manage hosts file
- add local SSL lifecycle

Deliverable:

- projects auto-map to local HTTPS URLs

## Phase 4: Packages and Version Switching

- package index
- runtime install/update/remove
- multi-version PHP and Node switching
- stack profiles

Deliverable:

- users can manage multiple runtime versions cleanly

## Phase 5: Productivity Features

- terminal tabs
- mail module
- quick app templates
- database/admin launchers
- custom scripts and aliases

Deliverable:

- daily-driver workflow is faster than Laragon for common use cases

## Phase 6: Sharing, Repair, and Polish

- tunneling/share features
- diagnostics center
- one-click fixes
- import/export and backup
- onboarding polish

Deliverable:

- beta-ready product with repair tooling

## 12. Suggested Implementation Order for Maximum Value

If we want fastest path to a usable product, build in this exact order:

1. Portable app shell + settings + tray
2. Supervisor + service lifecycle
3. Project scanning
4. VHost + hosts automation
5. SSL automation
6. Package manager + version switching
7. Logs + diagnostics
8. Built-in terminal
9. Profiles
10. Mail and sharing
11. Quick app templates
12. Import/export and repair polish

## 13. Risks and Hard Parts

### 13.1 True Parity Risk

- Laragon behavior includes many convenience details not obvious from surface-level docs.
- We will need side-by-side testing against Laragon for accuracy.

### 13.2 Privileged Operations

- Editing hosts, trusting certs, registering context menus, and changing PATH need elevation-safe flows.

### 13.3 Portable WinUI 3 Complexity

- WinUI 3 is suitable for the shell, but tray, shell integration, elevation, and terminal hosting will require Win32 interop.

### 13.4 Runtime Packaging

- Managing many third-party runtimes means ongoing work for mirrors, checksums, upgrade paths, and config templates.

### 13.5 Support Burden

- Once users depend on auto-generated configs and packages, diagnostics and repair quality becomes as important as core features.

## 14. Quality Strategy

Must-have testing layers:

- unit tests for config and domain logic
- integration tests for process orchestration
- snapshot/template tests for generated configs
- E2E tests for install/start/project-discovery/basic browsing flows
- regression suite for package install and runtime switching

Manual validation matrix:

- Windows 10 and 11
- non-admin launch
- admin-approved flows
- path with spaces
- portable folder moved to a new drive
- port conflict scenarios
- SSL trust repair
- runtime switching across multiple projects

## 15. Team Recommendation

For a serious parity effort, ideal minimum team:

- 1 desktop engineer for WinUI shell and Windows integration
- 1 platform engineer for supervisor, packages, runtimes, SSL, hosts, services
- 1 QA/product engineer for parity matrix, UX validation, regression coverage

Approximate delivery:

- MVP with core stack control: 8 to 12 weeks
- strong beta with most Laragon-like workflows: 16 to 24 weeks
- near-parity with polish and repair tooling: 24 to 36 weeks

## 16. Practical Recommendation

Do not start by chasing every Laragon feature equally.

Define 3 release bands:

- `R1 Core`: service control, projects, domains, SSL, logs
- `R2 Power`: package manager, version switching, terminal, profiles, mail
- `R3 Parity+`: sharing, quick app templates, repair center, import/export, deep customization

This still supports the "match Laragon" goal, but reduces delivery risk and lets us validate architecture before package complexity explodes.

## 17. Immediate Next Steps

1. Freeze the parity matrix against the exact Laragon version to benchmark.
2. Decide whether `Locora` is portable-only or portable + installer.
3. Create a WinUI 3 proof-of-concept with:
   - Navigation shell
   - tray integration
   - supervisor IPC
   - one managed service
4. Define the package manifest schema.
5. Define the local runtime folder layout and config format.
