namespace ND_Explorer.Services;

public interface IDialogService
{
    string? Prompt(string title, string message, string initialValue = "");
    bool Confirm(string title, string message, bool isDestructive = false);
}
