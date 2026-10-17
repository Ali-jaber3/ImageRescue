using System.IO;
using System.Text.Json;

namespace ImageRescue.App.Recovery;

public static class ResultFiles
{
    public static void ValidateBaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 ||
            name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 ||
            name.Any(char.IsControl) || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("Use 1–80 characters without <>:\"/\\|?*, trailing dots or spaces.");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
            stem[3] is >= '0' and <= '9')
            throw new ArgumentException("This filename is reserved by Windows.");
    }

    public static RecoveredImage Rename(string folder, RecoveredImage item, string newBaseName)
    {
        ValidateBaseName(newBaseName);
        string newName = newBaseName + Path.GetExtension(item.FileName);
        if (newName.Equals(item.FileName, StringComparison.Ordinal)) return item;
        if (newName.Equals(item.FileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a name that differs by more than letter case.");
        string oldPath = Path.Combine(folder, item.FileName), newPath = Path.Combine(folder, newName);
        if (File.Exists(newPath)) throw new IOException("A file with this name already exists.");
        string reportPath = Path.Combine(folder, "scan-session.json");
        var report = JsonSerializer.Deserialize<ScanReport>(File.ReadAllText(reportPath))
            ?? throw new IOException("Session report is missing or invalid.");
        int index = report.Files.FindIndex(f => f.FileName == item.FileName);
        if (index < 0) throw new IOException("The image was not found in the session report.");
        var renamed = item with { FileName = newName };
        report.Files[index] = renamed;
        string temporary = reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        bool moved = false;
        try
        {
            File.Move(oldPath, newPath, false);
            moved = true;
            File.Move(temporary, reportPath, true);
            return renamed;
        }
        catch
        {
            if (moved) File.Move(newPath, oldPath, false);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
