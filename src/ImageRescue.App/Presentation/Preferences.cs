using System.IO;
using System.Text.Json;

namespace ImageRescue.App.Presentation;

public sealed record Preferences(string Language = "ar", string Theme = "dark", string Destination = "");

public static class PreferenceStore
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageRescue", "settings.json");

    public static Preferences Load(string? path = null)
    {
        try
        {
            var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path ?? DefaultPath));
            if (p is null) return new();
            string[] languages = ["en", "ar", "zh-Hans", "ru", "es", "fr", "de", "pt", "hi", "ja"];
            return p with
            {
                Language = languages.Contains(p.Language) ? p.Language : "ar",
                Theme = p.Theme is "dark" or "light" or "soft" ? p.Theme : "dark",
                Destination = p.Destination ?? ""
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(); }
    }

    public static bool Save(Preferences preferences, string? path = null)
    {
        string target = path ?? DefaultPath;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
