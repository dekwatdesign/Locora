using Locora.App.Models;
using Locora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Locora.App.Views;

public sealed partial class TerminalPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public TerminalPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }

    private void OnTerminalInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || !ViewModel.SendTerminalInputCommand.CanExecute(null))
        {
            return;
        }

        ViewModel.SendTerminalInputCommand.Execute(null);
        args.Handled = true;
    }

    private void OnCloseTerminalTabClick(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: TerminalSession session } ||
            !ViewModel.CloseTerminalSessionCommand.CanExecute(session))
        {
            return;
        }

        ViewModel.CloseTerminalSessionCommand.Execute(session);
    }
}
