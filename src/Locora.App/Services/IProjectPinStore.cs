namespace Locora.App.Services;

public interface IProjectPinStore
{
    Task<IReadOnlyCollection<string>> LoadPinnedProjectKeysAsync(CancellationToken cancellationToken = default);

    Task SavePinnedProjectKeysAsync(IEnumerable<string> projectKeys, CancellationToken cancellationToken = default);
}
