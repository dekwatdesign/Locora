using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Locora.App.Models;

public sealed class ShareUrlSessionCard : ObservableObject
{
    private string _publicUrl = string.Empty;
    private string _state;
    private string _details;
    private bool _isRunning = true;
    private DateTimeOffset _lastUpdatedAt;

    public ShareUrlSessionCard(
        string key,
        string profileKey,
        string profileName,
        string provider,
        string localUrl,
        string commandText,
        string sessionId,
        string sessionTitle,
        string projectName,
        DateTimeOffset startedAt,
        IRelayCommand<ShareUrlSessionCard> copyCommand,
        IRelayCommand<ShareUrlSessionCard> openCommand,
        IRelayCommand<ShareUrlSessionCard> stopCommand)
    {
        Key = key;
        ProfileKey = profileKey;
        ProfileName = profileName;
        Provider = provider;
        LocalUrl = localUrl;
        CommandText = commandText;
        SessionId = sessionId;
        SessionTitle = sessionTitle;
        ProjectName = projectName;
        StartedAt = startedAt;
        _lastUpdatedAt = startedAt;
        _state = "Starting";
        _details = "Waiting for tunnel provider output.";
        CopyCommand = copyCommand;
        OpenCommand = openCommand;
        StopCommand = stopCommand;
    }

    public string Key { get; }

    public string ProfileKey { get; }

    public string ProfileName { get; }

    public string Provider { get; }

    public string LocalUrl { get; }

    public string CommandText { get; }

    public string SessionId { get; }

    public string SessionTitle { get; }

    public string ProjectName { get; }

    public DateTimeOffset StartedAt { get; }

    public IRelayCommand<ShareUrlSessionCard> CopyCommand { get; }

    public IRelayCommand<ShareUrlSessionCard> OpenCommand { get; }

    public IRelayCommand<ShareUrlSessionCard> StopCommand { get; }

    public string PublicUrl
    {
        get => _publicUrl;
        private set
        {
            if (SetProperty(ref _publicUrl, value))
            {
                OnPropertyChanged(nameof(HasPublicUrl));
                OnPropertyChanged(nameof(PublicUrlLabel));
                OnPropertyChanged(nameof(CanCopy));
                OnPropertyChanged(nameof(CanOpen));
            }
        }
    }

    public string State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    public string Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    public DateTimeOffset LastUpdatedAt
    {
        get => _lastUpdatedAt;
        private set
        {
            if (SetProperty(ref _lastUpdatedAt, value))
            {
                OnPropertyChanged(nameof(LastUpdatedAtLabel));
            }
        }
    }

    public bool HasPublicUrl => !string.IsNullOrWhiteSpace(PublicUrl);

    public bool CanCopy => HasPublicUrl;

    public bool CanOpen => HasPublicUrl;

    public bool CanStop => IsRunning;

    public string PublicUrlLabel => HasPublicUrl ? PublicUrl : "Public URL pending";

    public string StateLabel => State;

    public string ProviderLabel => string.IsNullOrWhiteSpace(Provider)
        ? "Provider: custom"
        : $"Provider: {Provider}";

    public string TargetLabel => string.IsNullOrWhiteSpace(LocalUrl)
        ? "Target: unavailable"
        : $"Target: {LocalUrl}";

    public string ProjectLabel => string.IsNullOrWhiteSpace(ProjectName)
        ? "Project: none"
        : $"Project: {ProjectName}";

    public string StartedAtLabel => StartedAt.ToString("HH:mm:ss");

    public string LastUpdatedAtLabel => LastUpdatedAt.ToString("HH:mm:ss");

    public string CopyAutomationName => $"Copy share URL for {ProfileName}";

    public string OpenAutomationName => $"Open share URL for {ProfileName}";

    public string StopAutomationName => $"Stop sharing for {ProfileName}";

    public string SearchText => $"{Key} {ProfileKey} {ProfileName} {Provider} {LocalUrl} {PublicUrl} {State} {ProjectName} {SessionTitle}";

    public void MarkPublicUrl(string publicUrl)
    {
        PublicUrl = publicUrl;
        State = "Active";
        Details = "Public URL detected from tunnel output.";
        LastUpdatedAt = DateTimeOffset.Now;
    }

    public void MarkStopped(string details)
    {
        IsRunning = false;
        State = HasPublicUrl ? "Stopped with URL" : "Stopped";
        Details = details;
        LastUpdatedAt = DateTimeOffset.Now;
    }
}
