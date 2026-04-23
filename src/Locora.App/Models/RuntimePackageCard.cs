using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Locora.App.Models;

public sealed class RuntimePackageCard : ObservableObject
{
    private string _selectedVersion;

    public RuntimePackageCard(
        string id,
        string name,
        string family,
        string stateLabel,
        string versionLabel,
        string installedLabel,
        string sourceLabel,
        string pathLabel,
        string summary,
        string details,
        IReadOnlyList<string> availableVersions,
        IReadOnlyList<string> installedVersions,
        string selectedVersion,
        string activeVersion,
        bool supportsSwitching,
        bool isInstalled,
        bool isActive,
        ICommand switchVersionCommand)
    {
        Id = id;
        Name = name;
        Family = family;
        StateLabel = stateLabel;
        VersionLabel = versionLabel;
        InstalledLabel = installedLabel;
        SourceLabel = sourceLabel;
        PathLabel = pathLabel;
        Summary = summary;
        Details = details;
        AvailableVersions = availableVersions;
        InstalledVersions = installedVersions;
        _selectedVersion = selectedVersion;
        ActiveVersion = activeVersion;
        SupportsSwitching = supportsSwitching;
        IsInstalled = isInstalled;
        IsActive = isActive;
        SwitchVersionCommand = switchVersionCommand;
    }

    public string Id { get; }

    public string Name { get; }

    public string Family { get; }

    public string StateLabel { get; }

    public string VersionLabel { get; }

    public string InstalledLabel { get; }

    public string SourceLabel { get; }

    public string PathLabel { get; }

    public string Summary { get; }

    public string Details { get; }

    public IReadOnlyList<string> AvailableVersions { get; }

    public IReadOnlyList<string> InstalledVersions { get; }

    public string SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(CanSwitchVersion));
                OnPropertyChanged(nameof(SwitchAutomationName));
            }
        }
    }

    public string ActiveVersion { get; }

    public bool SupportsSwitching { get; }

    public bool IsInstalled { get; }

    public bool IsActive { get; }

    public ICommand SwitchVersionCommand { get; }

    public bool CanSwitchVersion => SupportsSwitching &&
        !string.IsNullOrWhiteSpace(SelectedVersion) &&
        !SelectedVersion.Equals(ActiveVersion, StringComparison.OrdinalIgnoreCase);

    public string SwitchAutomationName => $"Switch {Name} to {SelectedVersion}";
}
