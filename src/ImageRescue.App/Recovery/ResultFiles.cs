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
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9')
            throw new ArgumentException("This filename is reserved by Windows.");
    }

    public static ScanReport LoadReport(string folder) =>
        JsonSerializer.Deserialize<ScanReport>(File.ReadAllText(Path.Combine(folder, "scan-session.json")))
        ?? throw new IOException("Invalid session report.");

    public static void WriteReport(string folder, ScanReport report)
    {
        string target = Path.Combine(folder, "scan-session.json");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static RecoveredImage Rename(string folder, RecoveredImage item, string newBaseName)
    {
        ValidateBaseName(newBaseName);
        return RenamePlan(folder, [(item, newBaseName + Path.GetExtension(item.FileName))]).Single();
    }

    public static List<RecoveredImage> RenameBatch(string folder, IReadOnlyList<RecoveredImage> items,
        string prefix, int firstNumber = 1)
    {
        ValidateBaseName(prefix);
        if (items.Count == 0 || firstNumber < 1 || (long)firstNumber + items.Count > int.MaxValue)
            throw new ArgumentException("Invalid batch selection or starting number.");
        int width = Math.Max(3, (firstNumber + items.Count - 1).ToString().Length);
        var plan = items.Select((item, i) => (item,
            prefix + "_" + (firstNumber + i).ToString("D" + width) + Path.GetExtension(item.FileName))).ToList();
        return RenamePlan(folder, plan);
    }

    private static List<RecoveredImage> RenamePlan(string folder,
        IReadOnlyList<(RecoveredImage Item, string NewName)> plan)
    {
        var report = LoadReport(folder);
        var oldNames = plan.Select(p => p.Item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (oldNames.Count != plan.Count) throw new ArgumentException("Duplicate selection.");
        var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedOffsets = plan.Select(p => p.Item.Offset).ToHashSet();
        foreach (var (item, name) in plan)
        {
            ValidateBaseName(Path.GetFileNameWithoutExtension(name));
            if (Path.GetFileName(name) != name || !newNames.Add(name)) throw new ArgumentException("Invalid or duplicate name.");
            if (!report.Files.Any(f => f.Offset == item.Offset && f.FileName == item.FileName && f.IsSaved == item.IsSaved))
                throw new IOException("Result no longer matches the session report.");
            if (report.Files.Any(f => !selectedOffsets.Contains(f.Offset) && f.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("A result already uses this name.");
            if (File.Exists(Path.Combine(folder, name)) && !oldNames.Contains(name))
                throw new IOException("A file already uses this name.");
            if (!item.IsSaved && File.Exists(Path.Combine(folder, name)))
                throw new IOException("A file already uses this name.");
        }
        var staged = new List<(string Old, string Temporary, string New)>();
        int finalized = 0;
        try
        {
            foreach (var (item, name) in plan)
            {
                if (!item.IsSaved || item.FileName == name) continue;
                string old = Path.Combine(folder, item.FileName);
                string temporary = Path.Combine(folder, ".rename-" + Guid.NewGuid().ToString("N"));
                File.Move(old, temporary, false);
                staged.Add((old, temporary, Path.Combine(folder, name)));
            }
            foreach (var file in staged)
            {
                File.Move(file.Temporary, file.New, false);
                finalized++;
            }
            var results = new List<RecoveredImage>();
            foreach (var (item, name) in plan)
            {
                var renamed = item with { FileName = name };
                report.Files[report.Files.FindIndex(f => f.Offset == item.Offset)] = renamed;
                results.Add(renamed);
            }
            WriteReport(folder, report);
            return results;
        }
        catch
        {
            // Put all final names back into staging before restoring the old names,
            // so cycles (A->B, B->A) can be rolled back without collisions.
            for (int i = finalized - 1; i >= 0; i--) File.Move(staged[i].New, staged[i].Temporary, false);
            for (int i = staged.Count - 1; i >= 0; i--) File.Move(staged[i].Temporary, staged[i].Old, false);
            throw;
        }
    }

    public static ScanReport SaveSelected(Stream source, string folder, IReadOnlyList<RecoveredImage> selected,
        CancellationToken token, IProgress<RecoveredImage>? progress = null, PauseController? pause = null, bool allowChanged = false)
    {
        var report = LoadReport(folder);
        if (source.Length != report.SourceBytes) throw new IOException("Source size changed since scanning.");
        var offsets = new HashSet<long>();
        foreach (var item in selected)
        {
            if (!offsets.Add(item.Offset)) throw new ArgumentException("Duplicate selection.");
            var match = report.Files.SingleOrDefault(f => f.Offset == item.Offset);
            if (match is null || match.FileName != item.FileName || match.Sha256 != item.Sha256)
                throw new IOException("Result no longer matches the session report.");
            ValidateBaseName(Path.GetFileNameWithoutExtension(item.FileName));
            if (Path.GetFileName(item.FileName) != item.FileName) throw new IOException("Invalid filename.");
        }
        try
        {
            foreach (var item in selected)
            {
                token.ThrowIfCancellationRequested(); pause?.Checkpoint(token);
                int index = report.Files.FindIndex(f => f.Offset == item.Offset);
                if (report.Files[index].IsSaved) continue;
                if (item.Offset < 0 || item.Bytes <= 0 || item.Offset > source.Length - item.Bytes)
                    throw new IOException("Invalid image range.");
                string actualHash = Carver.Export(source, item.Offset, item.Bytes, Path.Combine(folder, item.FileName),
                    token, pause, item.Sha256, allowChanged);
                bool changed = !actualHash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase);
                var saved = item with { IsSaved = true, Sha256 = actualHash,
                    ScannedSha256 = item.ScannedSha256 ?? item.Sha256, SourceChanged = changed };
                report.Files[index] = saved;
                progress?.Report(saved);
            }
        }
        finally { WriteReport(folder, report); }
        return report;
    }
}
