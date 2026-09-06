using ND_Explorer.Models;

namespace ND_Explorer.Services;

public interface ISettingsService
{
    ExplorerSettings Current { get; }
    void Save();
}
