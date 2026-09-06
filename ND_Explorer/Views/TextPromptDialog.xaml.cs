using System.Windows;

namespace ND_Explorer.Views;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog(string title, string message, string initialValue)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ValueBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            ValueBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
