using System.Windows;
using ND_Explorer.Views;

namespace ND_Explorer.Services;

public sealed class DialogService : IDialogService
{
    public string? Prompt(string title, string message, string initialValue = "")
    {
        var dialog = new TextPromptDialog(title, message, initialValue)
        {
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public bool Confirm(string title, string message, bool isDestructive = false)
    {
        var icon = isDestructive ? MessageBoxImage.Warning : MessageBoxImage.Question;
        return MessageBox.Show(
                   Application.Current.MainWindow,
                   message,
                   title,
                   MessageBoxButton.YesNo,
                   icon,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
