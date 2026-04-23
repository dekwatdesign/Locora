using Locora.App.ViewModels;
using Locora.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Windows.Input;

namespace Locora.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = typeof(DashboardPage),
        ["services"] = typeof(ServicesPage),
        ["domains"] = typeof(DomainsPage),
        ["diagnostics"] = typeof(DiagnosticsPage),
        ["terminal"] = typeof(TerminalPage),
        ["logs"] = typeof(LogsPage),
        ["settings"] = typeof(SettingsPage)
    };

    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.GetService<MainWindowViewModel>();

        InitializeComponent();
        ShellNavigationView.DataContext = ViewModel;
        ViewModel.NavigationRequested += OnViewModelNavigationRequested;

        NavigateTo("dashboard");

        _ = ViewModel.InitializeAsync();
    }

    public void NavigateTo(string tag)
    {
        if (!_pages.TryGetValue(tag, out var pageType))
        {
            return;
        }

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }

        var selectedItem = ShellNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));

        if (selectedItem is not null && !ReferenceEquals(ShellNavigationView.SelectedItem, selectedItem))
        {
            ShellNavigationView.SelectedItem = selectedItem;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        NavigateTo(tag);
    }

    private void OnViewModelNavigationRequested(object? sender, string tag)
    {
        NavigateTo(tag);
    }

    private void OnCommandPaletteButtonClick(object sender, RoutedEventArgs args)
    {
        FocusCommandPalette();
    }

    private void OnCommandPaletteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusCommandPalette();
        args.Handled = true;
    }

    private void OnCommandPaletteTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var suggestions = FindCommandPaletteItems(sender.Text).Take(12).ToList();
        sender.ItemsSource = suggestions;
        sender.IsSuggestionListOpen = suggestions.Count > 0;
    }

    private async void OnCommandPaletteQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var item = args.ChosenSuggestion as CommandPaletteItem ??
            FindCommandPaletteItems(args.QueryText).FirstOrDefault();

        await ExecuteCommandPaletteItemAsync(item);
    }

    private async void OnCommandPaletteSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        await ExecuteCommandPaletteItemAsync(args.SelectedItem as CommandPaletteItem);
    }

    private void FocusCommandPalette()
    {
        CommandPaletteBox.Focus(FocusState.Programmatic);
        CommandPaletteBox.ItemsSource = FindCommandPaletteItems(CommandPaletteBox.Text).Take(12).ToList();
        CommandPaletteBox.IsSuggestionListOpen = true;
    }

    private IEnumerable<CommandPaletteItem> FindCommandPaletteItems(string query)
    {
        var terms = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in CreateCommandPaletteItems())
        {
            if (terms.Length == 0 || terms.All(term => item.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                yield return item;
            }
        }
    }

    private IEnumerable<CommandPaletteItem> CreateCommandPaletteItems()
    {
        yield return new CommandPaletteItem("Go to Dashboard", "Open runtime overview and recent projects.", "dashboard home overview projects", () => NavigateAsync("dashboard"));
        yield return new CommandPaletteItem("Go to Services", "Manage Nginx, Apache, databases, cache, and Mailpit.", "services start stop nginx apache database cache mailpit", () => NavigateAsync("services"));
        yield return new CommandPaletteItem("Go to Domains & Hosts", "Manage local domains, hosts preview, vhosts, and SSL.", "domains hosts vhosts ssl certificates tld", () => NavigateAsync("domains"));
        yield return new CommandPaletteItem("Go to Diagnostics", "Inspect health issues, permissions, ports, and repair actions.", "diagnostics health ports permissions repair report", () => NavigateAsync("diagnostics"));
        yield return new CommandPaletteItem("Go to Terminal", "Open built-in project terminal sessions.", "terminal conpty shell project command", () => NavigateAsync("terminal"));
        yield return new CommandPaletteItem("Go to Logs", "Open supervisor and service logs.", "logs stdout stderr service supervisor", () => NavigateAsync("logs"));
        yield return new CommandPaletteItem("Go to Settings", "Open app paths, preferences, and diagnostic report actions.", "settings preferences paths report", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Go to Runtime Versions", "Open PHP, Node.js, Python, and Java runtime version switching.", "settings runtime versions switch php node python java", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Go to Stack Profiles", "Open active stack profile selection and package profile settings.", "settings stack profiles active services packages start all", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Go to Custom Tools", "Open custom tool menu settings.", "settings custom tools menu shortcuts launch", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Review Runtime Backup Guidance", "Open Settings to review runtime backup paths and restore order.", "settings runtime backup guidance data bin package cache restore", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Review PATH Environment Scripts", "Open Settings to inspect CurrentUser PATH and LOCORA variable scripts.", "settings path environment variables scripts current user", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Review App Updates", "Open Settings to inspect app update manifest configuration and cached update plans.", "settings app update release manifest version", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Review Portable Distribution", "Open Settings to inspect portable ZIP packaging automation.", "settings portable distribution packaging release zip installer", () => NavigateAsync("settings"));
        yield return new CommandPaletteItem("Go to Local Tunnels", "Open external access and local tunnel profiles.", "domains local tunnels share external access public url", () => NavigateAsync("domains"));

        yield return new CommandPaletteItem("Refresh Snapshot", "Reload services, projects, health, and diagnostics.", "refresh reload snapshot", () => ExecuteCommandAsync(ViewModel.RefreshCommand));
        yield return new CommandPaletteItem("Start All Services", "Start all enabled services through the supervisor.", "start all services run", () => ExecuteCommandAsync(ViewModel.StartAllCommand));
        yield return new CommandPaletteItem("Stop All Services", "Stop all managed services.", "stop all services shutdown", () => ExecuteCommandAsync(ViewModel.StopAllCommand));
        yield return new CommandPaletteItem("Repair Runtime Configs", "Regenerate common runtime configs and validate Nginx.", "repair runtime configs nginx mariadb redis mailpit", () => ExecuteCommandAsync(ViewModel.RepairRuntimeCommand));
        yield return new CommandPaletteItem("Repair Hosts & VHosts", "Regenerate hosts preview plus Nginx and Apache vhosts.", "repair hosts vhosts domains apache nginx", () => ExecuteCommandAsync(ViewModel.RepairDomainsCommand));
        yield return new CommandPaletteItem("Validate Nginx Config", "Run Nginx config validation before startup.", "validate nginx config test", () => ExecuteCommandAsync(ViewModel.ValidateNginxConfigCommand));
        yield return new CommandPaletteItem("Validate Portable Relocation", "Refresh diagnostics and check for absolute paths that may break after moving Locora.", "validate portable relocation move drive paths absolute", () => ExecuteCommandAsync(ViewModel.RefreshCommand));
        yield return new CommandPaletteItem("Generate Local SSL", "Generate the Locora CA and per-project certificates.", "generate local ssl certificates ca", () => ExecuteCommandAsync(ViewModel.GenerateLocalSslCommand));
        yield return new CommandPaletteItem("Repair Local SSL", "Repair missing, expired, invalid, or stale project certificates.", "repair local ssl certificates ca", () => ExecuteCommandAsync(ViewModel.RepairLocalSslCommand));
        yield return new CommandPaletteItem("Show First-Run Guide", "Reveal the dashboard setup checklist.", "onboarding first run guide setup checklist", () => ExecuteCommandAsync(ViewModel.ShowFirstRunOnboardingCommand));
        yield return new CommandPaletteItem("Export Diagnostic Report", "Write a Markdown diagnostic report to the logs folder.", "export diagnostic report markdown support", () => ExecuteCommandAsync(ViewModel.ExportDiagnosticReportCommand));
        yield return new CommandPaletteItem("Back Up Config", "Create a timestamped zip archive of usr/config.", "config backup archive zip settings migration", () => ExecuteCommandAsync(ViewModel.BackupConfigCommand));
        yield return new CommandPaletteItem("Restore Config Backup", "Restore a configuration backup zip into usr/config after creating a pre-restore backup.", "config restore backup archive zip rollback settings migration", () => ExecuteCommandAsync(ViewModel.RestoreConfigCommand));
        yield return new CommandPaletteItem("Install or Update Packages", "Sync, verify, and extract the active package selections into the portable runtime roots.", "packages install update runtimes tools extract current", () => ExecuteCommandAsync(ViewModel.InstallOrUpdatePackagesCommand));
        yield return new CommandPaletteItem("Sync Package Downloads", "Copy or download locked package artifacts into the cache.", "packages downloads cache sync artifacts runtimes", () => ExecuteCommandAsync(ViewModel.SyncPackageDownloadsCommand));
        yield return new CommandPaletteItem("Extract Package Archives", "Unpack cached package archives into the portable bin roots and refresh current runtime paths.", "packages extract archives runtimes bin current install", () => ExecuteCommandAsync(ViewModel.ExtractPackageArchivesCommand));
        yield return new CommandPaletteItem("Validate Package Checksums", "Refresh package cache status and validate SHA-256 for cached artifacts.", "packages checksum sha256 validate cache verify", () => ExecuteCommandAsync(ViewModel.RefreshCommand));
        yield return new CommandPaletteItem("Save Active Environment", "Save the current running services and active package selections as a stack profile.", "stack profile save active environment services packages", () => ExecuteCommandAsync(ViewModel.SaveActiveEnvironmentCommand));
        yield return new CommandPaletteItem("Export Stack Profiles", "Write stack profiles to the configured transfer JSON file.", "stack profiles export backup share json", () => ExecuteCommandAsync(ViewModel.ExportStackProfilesCommand));
        yield return new CommandPaletteItem("Import Stack Profiles", "Merge stack profiles from the configured transfer JSON file.", "stack profiles import restore merge json", () => ExecuteCommandAsync(ViewModel.ImportStackProfilesCommand));
        yield return new CommandPaletteItem("Preview Laragon Import", "Scan a Laragon root and preview imported project overrides.", "laragon import migration preview projects www config", () => ExecuteCommandAsync(ViewModel.PreviewLaragonImportCommand));
        yield return new CommandPaletteItem("Import Laragon Projects", "Merge Laragon www projects into Locora project discovery.", "laragon import migration projects www config", () => ExecuteCommandAsync(ViewModel.ImportLaragonProjectsCommand));
        yield return new CommandPaletteItem("Open Package Sources", "Inspect bundled or custom package source definitions.", "packages sources manifest registry runtimes", () => ExecuteCommandAsync(ViewModel.OpenPackageSourcesSettingsFileCommand));
        yield return new CommandPaletteItem("Open Packages Lock", "Inspect selected runtime versions and installed package records.", "packages lock versions installs state", () => ExecuteCommandAsync(ViewModel.OpenPackagesLockSettingsFileCommand));
        yield return new CommandPaletteItem("Open Stack Profiles", "Inspect profile service groups and package selections.", "stack profiles open profiles json services packages active", () => ExecuteCommandAsync(ViewModel.OpenProfilesSettingsFileCommand));
        yield return new CommandPaletteItem("Open Stack Profiles Folder", "Open the profile import/export folder.", "stack profiles folder import export backups open", () => ExecuteCommandAsync(ViewModel.OpenProfilesRootCommand));
        yield return new CommandPaletteItem("Open Terminal Commands", "Edit preset commands and aliases for the built-in terminal.", "terminal commands aliases presets quick shell", () => ExecuteCommandAsync(ViewModel.OpenTerminalCommandsSettingsFileCommand));
        yield return new CommandPaletteItem("Open Custom Tools", "Edit custom menu tools for URLs, files, folders, editors, and terminal commands.", "custom tools menu shortcuts json open", () => ExecuteCommandAsync(ViewModel.OpenCustomToolsSettingsFileCommand));
        yield return new CommandPaletteItem("Reload Custom Tools", "Reload custom menu tools from custom-tools.json.", "custom tools menu reload refresh", () => ExecuteCommandAsync(ViewModel.ReloadCustomToolsCommand));
        yield return new CommandPaletteItem("Open Local Tunnels", "Edit local tunnel profiles for external access commands.", "local tunnels share external access public url json open", () => ExecuteCommandAsync(ViewModel.OpenLocalTunnelsSettingsFileCommand));
        yield return new CommandPaletteItem("Reload Local Tunnels", "Reload local tunnel profiles from local-tunnels.json.", "local tunnels share reload refresh external access", () => ExecuteCommandAsync(ViewModel.ReloadLocalTunnelsCommand));
        yield return new CommandPaletteItem("Review External Access Consent", "Open the tunnel consent warning before starting external sharing.", "local tunnels consent warning external access share required", () => NavigateAsync("domains"));
        yield return new CommandPaletteItem("Review Firewall Guidance", "Open firewall and network guidance for LAN testing and public tunnel sharing.", "firewall network guidance ports lan tunnel inbound outbound", () => NavigateAsync("domains"));
        yield return new CommandPaletteItem("Clear Stopped Share URLs", "Remove stopped share URL sessions from the lifecycle list.", "share url lifecycle clear stopped tunnels", () => ExecuteCommandAsync(ViewModel.ClearStoppedShareUrlsCommand));
        yield return new CommandPaletteItem("Open Aliases Folder", "Open the portable aliases folder that is added to terminal PATH.", "terminal aliases folder path scripts", () => ExecuteCommandAsync(ViewModel.OpenAliasesRootCommand));
        yield return new CommandPaletteItem("Open Portable Root in Editor", "Test the preferred editor integration against the portable root.", "editor integration open preferred root", () => ExecuteCommandAsync(ViewModel.OpenEnvironmentRootInEditorCommand));
        yield return new CommandPaletteItem("Open Projects Root in Editor", "Open the projects root with the preferred editor target selection.", "editor integration open preferred projects root", () => ExecuteCommandAsync(ViewModel.OpenProjectRootInEditorCommand));
        yield return new CommandPaletteItem("Refresh Shell Context Menu Files", "Regenerate Windows Explorer context menu registry files for this app path.", "shell explorer context menu registry refresh regenerate", () => ExecuteCommandAsync(ViewModel.RefreshShellContextMenuFilesCommand));
        yield return new CommandPaletteItem("Open Shell Context Menu Install File", "Open the generated registry file that adds Locora Explorer context menus.", "shell explorer context menu registry install open", () => ExecuteCommandAsync(ViewModel.OpenShellContextMenuInstallFileCommand));
        yield return new CommandPaletteItem("Open Shell Context Menu Uninstall File", "Open the generated registry file that removes Locora Explorer context menus.", "shell explorer context menu registry uninstall remove open", () => ExecuteCommandAsync(ViewModel.OpenShellContextMenuUninstallFileCommand));
        yield return new CommandPaletteItem("Refresh Environment Scripts", "Regenerate CurrentUser PATH and LOCORA variable scripts.", "path environment variables current user refresh regenerate scripts", () => ExecuteCommandAsync(ViewModel.RefreshUserEnvironmentFilesCommand));
        yield return new CommandPaletteItem("Open Environment Install Script", "Open the generated PowerShell script that adds user-level Locora environment values.", "path environment variables install powershell current user open", () => ExecuteCommandAsync(ViewModel.OpenUserEnvironmentApplyScriptCommand));
        yield return new CommandPaletteItem("Open Environment Uninstall Script", "Open the generated PowerShell script that removes user-level Locora environment values.", "path environment variables uninstall remove powershell current user open", () => ExecuteCommandAsync(ViewModel.OpenUserEnvironmentRemoveScriptCommand));
        yield return new CommandPaletteItem("Check for App Updates", "Read the configured release manifest and refresh the app update plan.", "app update check release manifest version", () => ExecuteCommandAsync(ViewModel.CheckForAppUpdatesCommand));
        yield return new CommandPaletteItem("Open App Update Release Page", "Open the release page from the current app update status.", "app update release page open", () => ExecuteCommandAsync(ViewModel.OpenAppUpdateReleasePageCommand));
        yield return new CommandPaletteItem("Open App Update Plan", "Open the cached app update plan JSON.", "app update plan json open cache", () => ExecuteCommandAsync(ViewModel.OpenAppUpdatePlanCommand));
        yield return new CommandPaletteItem("Refresh Portable Distribution Files", "Regenerate the portable ZIP packaging script, plan, README, and manifest template.", "portable distribution packaging release zip refresh script", () => ExecuteCommandAsync(ViewModel.RefreshPortableDistributionFilesCommand));
        yield return new CommandPaletteItem("Open Portable Distribution Script", "Open the generated PowerShell script for building a portable ZIP release.", "portable distribution packaging release zip powershell open", () => ExecuteCommandAsync(ViewModel.OpenPortableDistributionScriptCommand));
        yield return new CommandPaletteItem("Open Portable Distribution Plan", "Open the generated portable distribution plan JSON.", "portable distribution packaging release zip plan open", () => ExecuteCommandAsync(ViewModel.OpenPortableDistributionPlanCommand));
        yield return new CommandPaletteItem("Open Shell Integration Folder", "Open generated shell integration files.", "shell explorer context menu registry folder open", () => ExecuteCommandAsync(ViewModel.OpenShellIntegrationRootCommand));
        yield return new CommandPaletteItem("Open Package Manifests Folder", "Inspect bundled package manifests that describe runtimes and tools.", "packages manifests folder catalog runtimes tools", () => ExecuteCommandAsync(ViewModel.OpenPackageManifestsRootCommand));
        yield return new CommandPaletteItem("Open Package Cache Folder", "Inspect the local package artifact cache.", "packages cache folder downloads artifacts", () => ExecuteCommandAsync(ViewModel.OpenPackageCacheRootCommand));
        yield return new CommandPaletteItem("Open Config Backups", "Open configuration backup archives.", "config backups folder archive migration open", () => ExecuteCommandAsync(ViewModel.OpenConfigBackupRootCommand));

        foreach (var preset in ViewModel.ServicePresets.Where(preset => preset.CanRun))
        {
            var presetSearchText = $"{preset.DisplayName} {preset.Key} {preset.State} {preset.Description} {preset.ServicesLabel} {preset.TagsLabel}";
            yield return new CommandPaletteItem(
                $"Start Service Preset: {preset.DisplayName}",
                preset.Summary,
                $"service preset start group services {presetSearchText}",
                () => ExecuteCommandAsync(preset.StartPresetCommand, preset));
            yield return new CommandPaletteItem(
                $"Stop Service Preset: {preset.DisplayName}",
                preset.Summary,
                $"service preset stop group services {presetSearchText}",
                () => ExecuteCommandAsync(preset.StopPresetCommand, preset));
        }

        foreach (var command in ViewModel.TerminalCommands)
        {
            yield return new CommandPaletteItem(
                $"Run Terminal Command: {command.DisplayLabel}",
                command.Summary,
                $"terminal command alias preset quick run {command.SearchText}",
                () => ExecuteCommandAsync(ViewModel.RunTerminalCommandCommand, command));
        }

        foreach (var tool in ViewModel.CustomTools)
        {
            yield return new CommandPaletteItem(
                $"Run Custom Tool: {tool.DisplayName}",
                tool.Summary,
                $"custom tools menu launch run {tool.SearchText}",
                () => ExecuteCommandAsync(tool.RunCommand, tool));
        }

        foreach (var tunnel in ViewModel.LocalTunnels)
        {
            yield return new CommandPaletteItem(
                $"Start Local Tunnel: {tunnel.DisplayName}",
                tunnel.Summary,
                $"local tunnel share external access public url start consent warning {tunnel.SearchText}",
                () => ExecuteCommandAsync(tunnel.StartCommand, tunnel));
        }

        foreach (var shareSession in ViewModel.ShareUrlSessions)
        {
            yield return new CommandPaletteItem(
                $"Copy Share URL: {shareSession.ProfileName}",
                shareSession.PublicUrlLabel,
                $"share url copy public tunnel lifecycle {shareSession.SearchText}",
                () => ExecuteCommandAsync(shareSession.CopyCommand, shareSession));
            yield return new CommandPaletteItem(
                $"Open Share URL: {shareSession.ProfileName}",
                shareSession.PublicUrlLabel,
                $"share url open public tunnel lifecycle browser {shareSession.SearchText}",
                () => ExecuteCommandAsync(shareSession.OpenCommand, shareSession));
            yield return new CommandPaletteItem(
                $"Stop Share URL: {shareSession.ProfileName}",
                shareSession.StateLabel,
                $"share url stop public tunnel lifecycle close {shareSession.SearchText}",
                () => ExecuteCommandAsync(shareSession.StopCommand, shareSession));
        }

        foreach (var service in ViewModel.Services.Where(service => service.SupportsDatabaseAdminTool))
        {
            var serviceSearchText = $"{service.Name} {service.Key} {service.Version} {service.Port} {service.Note}";
            yield return new CommandPaletteItem(
                $"Open DB Admin: {service.Name}",
                "Launch a detected desktop database admin tool for this service.",
                $"database admin launch tool sql {serviceSearchText}",
                () => ExecuteCommandAsync(ViewModel.OpenDatabaseAdminToolCommand, service));
            yield return new CommandPaletteItem(
                $"Copy DB Connection: {service.Name}",
                "Copy host, port, URL, and credential hints for this database service.",
                $"database connection copy snippet sql {serviceSearchText}",
                () => ExecuteCommandAsync(ViewModel.CopyDatabaseConnectionCommand, service));
        }

        foreach (var profile in ViewModel.StackProfiles.Where(profile => profile.CanSelect))
        {
            var profileSearchText = $"{profile.DisplayName} {profile.Key} {profile.State} {profile.Description} {profile.ServicesLabel} {profile.PackagesLabel} {profile.TagsLabel}";
            yield return new CommandPaletteItem(
                $"Load Stack Profile: {profile.DisplayName}",
                profile.Summary,
                $"stack profile load select active services packages start all {profileSearchText}",
                () => ExecuteCommandAsync(profile.SelectProfileCommand, profile));
        }

        foreach (var project in ViewModel.Projects)
        {
            var projectSearchText = $"{project.Name} {project.Url} {project.Runtime} {project.Description} {project.OverrideSummary} {project.TagsLabel} {project.Folder}";
            yield return new CommandPaletteItem($"Open Site: {project.Name}", project.Url, $"project site browser open {projectSearchText}", () => ExecuteCommandAsync(project.OpenUrlCommand, project));
            yield return new CommandPaletteItem($"Open Folder: {project.Name}", project.Folder, $"project folder explorer open {projectSearchText}", () => ExecuteCommandAsync(project.OpenFolderCommand, project));
            yield return new CommandPaletteItem($"Reveal in Explorer: {project.Name}", project.Folder, $"project folder explorer reveal select context menu {projectSearchText}", () => ExecuteCommandAsync(project.RevealInExplorerCommand, project));
            yield return new CommandPaletteItem($"Copy Folder Path: {project.Name}", project.Folder, $"project folder path copy clipboard context menu {projectSearchText}", () => ExecuteCommandAsync(project.CopyFolderPathCommand, project));
            yield return new CommandPaletteItem($"Open Terminal: {project.Name}", project.Folder, $"project terminal shell conpty open {projectSearchText}", () => ExecuteCommandAsync(project.OpenTerminalCommand, project));
            yield return new CommandPaletteItem($"Open Editor: {project.Name}", project.Folder, $"project editor code open {projectSearchText}", () => ExecuteCommandAsync(project.OpenEditorCommand, project));
        }
    }

    private Task NavigateAsync(string tag)
    {
        NavigateTo(tag);
        return Task.CompletedTask;
    }

    private static Task ExecuteCommandAsync(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteCommandPaletteItemAsync(CommandPaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        CommandPaletteBox.Text = string.Empty;
        CommandPaletteBox.ItemsSource = null;
        CommandPaletteBox.IsSuggestionListOpen = false;
        await item.ExecuteAsync();
    }

    private sealed class CommandPaletteItem
    {
        public CommandPaletteItem(string title, string description, string searchText, Func<Task> executeAsync)
        {
            Title = title;
            Description = description;
            SearchText = $"{title} {description} {searchText}";
            ExecuteAsync = executeAsync;
        }

        public string Title { get; }

        public string Description { get; }

        public string SearchText { get; }

        public Func<Task> ExecuteAsync { get; }

        public override string ToString() => $"{Title} - {Description}";
    }
}
