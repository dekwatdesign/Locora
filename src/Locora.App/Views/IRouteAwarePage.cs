using Locora.App.Contracts;

namespace Locora.App.Views;

public interface IRouteAwarePage
{
    void ApplyRoute(LocoraShellRoute route);
}
