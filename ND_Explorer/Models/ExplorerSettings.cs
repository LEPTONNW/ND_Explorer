namespace ND_Explorer.Models;

public sealed class ExplorerSettings
{
    public string? LastPath { get; set; }
    public List<string> RecentPaths { get; set; } = [];
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 760;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool IsMaximized { get; set; }
}
