namespace Locora.App.Services;

public interface ILocoraStringResourceService
{
    string Language { get; }

    string Get(string key);
}
