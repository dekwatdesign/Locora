using Locora.App.Contracts;
using Xunit;

namespace Locora.Application.Tests;

public sealed class ShellRoutingTests
{
    [Theory]
    [InlineData("dashboard", LocoraShellDestination.Home, "overview")]
    [InlineData("services", LocoraShellDestination.Workbench, "services")]
    [InlineData("domains", LocoraShellDestination.Health, "domains")]
    [InlineData("diagnostics", LocoraShellDestination.Health, "issues")]
    [InlineData("logs", LocoraShellDestination.Advanced, "logs")]
    [InlineData("settings", LocoraShellDestination.Advanced, "settings")]
    public void Resolve_MapsLegacyAliases(string tag, LocoraShellDestination destination, string section)
    {
        var route = LocoraShellRoutes.Resolve(tag);

        Assert.Equal(destination, route.Destination);
        Assert.Equal(section, route.Section);
    }

    [Theory]
    [InlineData("workbench:services", LocoraShellDestination.Workbench, "services")]
    [InlineData("health/ssl", LocoraShellDestination.Health, "ssl")]
    [InlineData("advanced:packages", LocoraShellDestination.Advanced, "packages")]
    [InlineData("unknown", LocoraShellDestination.Home, "overview")]
    public void Resolve_NormalizesRouteAndSubroute(string tag, LocoraShellDestination destination, string section)
    {
        var route = LocoraShellRoutes.Resolve(tag);

        Assert.Equal(destination, route.Destination);
        Assert.Equal(section, route.Section);
    }

    [Fact]
    public void CreateNavigationCommands_ExposesExpectedCommandTargets()
    {
        var commands = LocoraShellRoutes.CreateNavigationCommands();

        Assert.Contains(commands, command => command.Title == "Go to Workbench" && command.RouteTag == "workbench");
        Assert.Contains(commands, command => command.Title == "Go to Runtime Versions" && command.RouteTag == "advanced:packages");
        Assert.Contains(commands, command => command.Title == "Go to Domains & SSL" && command.RouteTag == "health:domains");
    }

    [Theory]
    [InlineData(null, "Auto", "")]
    [InlineData("", "Auto", "")]
    [InlineData("Auto", "Auto", "")]
    [InlineData("en-US", "en-US", "en-US")]
    [InlineData("th-TH", "th-TH", "th-TH")]
    [InlineData("fr-FR", "Auto", "")]
    public void LanguagePreference_NormalizesAndResolvesFallback(string? input, string normalized, string resolved)
    {
        Assert.Equal(normalized, LocoraLanguagePreference.Normalize(input));
        Assert.Equal(resolved, LocoraLanguagePreference.ResolveOverride(input));
    }
}
