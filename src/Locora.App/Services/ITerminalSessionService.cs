using System.Collections.ObjectModel;
using Locora.App.Models;

namespace Locora.App.Services;

public interface ITerminalSessionService
{
    ObservableCollection<TerminalSession> Sessions { get; }

    TerminalSession StartSession(string title, ProjectTerminalLaunchProfile launchProfile);

    void SendInput(TerminalSession session, string input);

    void StopSession(TerminalSession session);

    void CloseSession(TerminalSession session);
}
