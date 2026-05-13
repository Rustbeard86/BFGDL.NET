using System.IO;
using System.Text.Json;

namespace BFGDL.NET.Services;

public sealed class GuiPreferences
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "gui-prefs.json");

    public bool LaunchOnPrimaryMonitor { get; set; } = true;

    public static GuiPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<GuiPreferences>(File.ReadAllText(FilePath))
                       ?? new GuiPreferences();
        }
        catch { }
        return new GuiPreferences();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch { }
    }
}
