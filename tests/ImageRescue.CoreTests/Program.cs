using ImageRescue.App.Recovery;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using System.IO.Compression;
using ImageRescue.App.Presentation;
using System.Diagnostics;

string work = Path.Combine(Path.GetTempPath(), "ImageRescueTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
int checks = 0;
try
{
    byte[] png = MakePng();
    // Structurally valid tiny JPEG fixture, containing an EOI inside APP metadata.
    // It deliberately tests marker parsing; this is not claimed as a decoded-pixel test.
    byte[] jpeg = [255,216,255,224,0,6,1,255,217,2,
        255,192,0,11,8,0,1,0,1,1,1,17,0,
        255,218,0,8,1,1,0,0,63,0,42,255,0,99,255,208,23,255,217];
    long boundary = 1024 * 1024;
    byte[] image = new byte[2 * 1024 * 1024 + 200];
    png.CopyTo(image, boundary - 3); // signature crosses scanner boundary
    jpeg.CopyTo(image, 2 * boundary - 1);
    var report = Run(image, "boundary");
    Check(report.Files.Count == 2, "PNG/JPEG found across scan-block boundaries");
    foreach (var f in report.Files)
    {
        byte[] expected = f.FileType == "PNG" ? png : jpeg;
        byte[] actual = File.ReadAllBytes(Path.Combine(work, "boundary", f.FileName));
        Check(actual.SequenceEqual(expected), "Recovered bytes exactly match fixture: " + f.FileType);
        Check(f.Sha256 == Convert.ToHexString(SHA256.HashData(expected)), "SHA256 matches: " + f.FileType);
    }
    Check(report.Files.Single(f => f.FileType == "JPG").Offset == 2 * boundary - 1,
        "JPEG metadata EOI did not truncate image");
    Check(JsonSerializer.Deserialize<ScanReport>(File.ReadAllText(Path.Combine(work,
        "boundary", "scan-session.json")))!.Files.Count == 2, "Session report round-trip");

    byte[] badCrc = (byte[])png.Clone(); badCrc[29] ^= 1;
    Check(Run(badCrc, "bad-crc").Files.Count == 0, "Corrupt PNG CRC rejected");
    Check(Run(png[..^3], "truncated-png").Files.Count == 0, "Truncated PNG rejected");
    Check(Run(jpeg[..^2], "truncated-jpeg").Files.Count == 0, "Truncated JPEG rejected");
    Check(Run([255,216,255,217], "fake-jpeg").Files.Count == 0, "Fake JPEG rejected");
    Check(Run([], "empty").Files.Count == 0, "Empty source handled");
    byte[] adjacent = [..png, ..png, ..jpeg];
    Check(Run(adjacent, "adjacent").Files.Count == 3, "Adjacent images are all recovered");
    var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    Check(Run(image, "cancel", cancellation.Token).Outcome == "Cancelled", "Cancellation records a report");
    using (var stream = new ChunkedStream(image))
    {
        var partial = Carver.Scan(stream, "short-read", Path.Combine(work, "short-read"), null, null, default);
        Check(partial.Files.Count == 2, "Short stream reads handled correctly");
    }
    using (var stream = new BrokenStream(image))
    {
        bool failed = false;
        try { Carver.Scan(stream, "io-error", Path.Combine(work, "io-error"), null, null, default); }
        catch (IOException) { failed = true; }
        Check(failed, "Read error propagated");
        Check(File.ReadAllText(Path.Combine(work, "io-error", "scan-session.json")).Contains("Failed"),
            "Read error produces a failed session report");
    }
    Check(!Directory.EnumerateFiles(work, "*.part", SearchOption.AllDirectories).Any(), "No unfinished output remains");
    foreach (string fixture in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures")))
    {
        byte[] original = File.ReadAllBytes(fixture);
        byte[] container = [..new byte[507], ..original, ..new byte[57]];
        byte[] before = SHA256.HashData(container);
        string testName = Path.GetFileNameWithoutExtension(fixture);
        var actualReport = Run(container, "fixture-" + testName);
        Check(actualReport.Files.Count == 1, "Real image fixture recovered: " + testName);
        var actualFile = actualReport.Files[0];
        Check(File.ReadAllBytes(Path.Combine(work, "fixture-" + testName,
            actualFile.FileName)).SequenceEqual(original), "Real image exact-byte match: " + testName);
        Check(SHA256.HashData(container).SequenceEqual(before), "Source unchanged: " + testName);
        Check(actualFile.Width == 23 && actualFile.Height == 17, "Pixel dimensions: " + testName);
        if (testName.StartsWith("camera-exif"))
        {
            Check(actualFile.Camera == "Canon EOS Test", "Camera EXIF: " + testName);
            Check(actualFile.DateTaken == "2026-10-22 12:34:56", "Capture date EXIF: " + testName);
        }
        else Check(actualFile.Camera is null && actualFile.DateTaken is null, "Missing EXIF is not invented: " + testName);
    }
    byte[] modesData = new byte[4096];
    png.CopyTo(modesData, 512);
    jpeg.CopyTo(modesData, 1777);
    using (var stream = new MemoryStream(modesData))
    {
        var quick = Carver.Scan(stream, "quick", Path.Combine(work, "quick"), null, null,
            default, ScanMode.Quick, namePrefix: "holiday");
        Check(quick.Files.Count == 1 && quick.Files[0].Offset == 512, "Quick checks only sector boundaries");
        Check(quick.Mode == "Quick", "Report records quick mode");
        Check(quick.Files[0].FileName.StartsWith("holiday_"), "Custom name prefix used");
    }
    Check(Run(modesData, "deep").Files.Count == 2, "Deep also finds unaligned images");
    using (var stream = new MemoryStream(image))
    using (var pause = new PauseController())
    using (var reached = new ManualResetEventSlim())
    {
        pause.Pause(); pause.Paused = reached.Set;
        var task = Task.Run(() => Carver.Scan(stream, "paused", Path.Combine(work, "paused"),
            null, null, default, ScanMode.Deep, pause));
        Check(reached.Wait(TimeSpan.FromSeconds(3)), "Worker acknowledges pause checkpoint");
        Check(!task.IsCompleted, "Scan remains blocked while paused");
        pause.Resume();
        Check(task.Wait(TimeSpan.FromSeconds(5)) && task.Result.Files.Count == 2, "Resume completes without duplicate results");
    }
    using (var stream = new MemoryStream(image))
    using (var pause = new PauseController())
    using (var reached = new ManualResetEventSlim())
    using (var cancel = new CancellationTokenSource())
    {
        pause.Pause(); pause.Paused = reached.Set;
        var task = Task.Run(() => Carver.Scan(stream, "paused-cancel", Path.Combine(work, "paused-cancel"),
            null, null, cancel.Token, ScanMode.Deep, pause));
        Check(reached.Wait(TimeSpan.FromSeconds(3)), "Second paused scan acknowledged");
        cancel.Cancel();
        Check(task.Wait(TimeSpan.FromSeconds(5)) && task.Result.Outcome == "Cancelled", "Cancel unblocks a paused scan");
    }
    string renameFolder = Path.Combine(work, "fixture-baseline");
    var renameReport = JsonSerializer.Deserialize<ScanReport>(File.ReadAllText(Path.Combine(renameFolder, "scan-session.json")))!;
    var originalItem = renameReport.Files.Single();
    var renamedItem = ResultFiles.Rename(renameFolder, originalItem, "my_photo");
    Check(File.Exists(Path.Combine(renameFolder, "my_photo.jpg")), "Rename changes the actual filename");
    Check(!File.Exists(Path.Combine(renameFolder, originalItem.FileName)), "Old filename no longer exists");
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(renameFolder, renamedItem.FileName))))
        == originalItem.Sha256, "Rename preserves every image byte");
    Check(JsonSerializer.Deserialize<ScanReport>(File.ReadAllText(Path.Combine(renameFolder,
        "scan-session.json")))!.Files.Single().FileName == "my_photo.jpg", "Rename updates session report");
    bool invalid = false;
    try { ResultFiles.Rename(renameFolder, renamedItem, "../outside"); }
    catch (ArgumentException) { invalid = true; }
    Check(invalid, "Path traversal filename rejected");
    File.WriteAllText(Path.Combine(renameFolder, "taken.jpg"), "untouched");
    bool collision = false;
    try { ResultFiles.Rename(renameFolder, renamedItem, "taken"); }
    catch (IOException) { collision = true; }
    Check(collision && File.ReadAllText(Path.Combine(renameFolder, "taken.jpg")) == "untouched",
        "Rename never overwrites an existing image");

    byte[] catalogueData = [..png, ..jpeg, ..png];
    string catalogueFolder = Path.Combine(work, "catalogue");
    using (var stream = new MemoryStream(catalogueData))
    {
        var catalogue = Carver.Scan(stream, "catalogue", catalogueFolder, null, null, default,
            options: new(SaveAll: false));
        Check(catalogue.Files.Count == 3 && catalogue.Files.All(f => !f.IsSaved), "Selection scan produces unsaved catalogue entries");
        Check(Directory.GetFiles(catalogueFolder).Select(Path.GetFileName).SequenceEqual(new[] { "scan-session.json" }),
            "No unselected images are written to disk");
        var selected = catalogue.Files[1];
        var renamedCatalogue = ResultFiles.Rename(catalogueFolder, selected, "chosen");
        Check(!File.Exists(Path.Combine(catalogueFolder, "chosen.jpg")), "Renaming an unsaved entry does not write image data");
        var savedReport = ResultFiles.SaveSelected(stream, catalogueFolder, [renamedCatalogue], default);
        Check(savedReport.Files.Count(f => f.IsSaved) == 1, "Only selected image is saved");
        Check(File.ReadAllBytes(Path.Combine(catalogueFolder, "chosen.jpg")).SequenceEqual(jpeg), "Selected export matches original bytes");
        Check(Directory.GetFiles(catalogueFolder, "*.png").Length == 0, "Unselected PNGs remain unwritten");
        Check(ResultFiles.LoadReport(catalogueFolder).Files.Count(f => f.IsSaved) == 1, "Selected export updates session report");
        ResultFiles.SaveSelected(stream, catalogueFolder, [savedReport.Files[1]], default);
        Check(Directory.GetFiles(catalogueFolder, "*.jpg").Length == 1, "Already-saved selection is idempotent");
    }
    using (var stream = new MemoryStream(catalogueData))
    {
        var onlyPng = Carver.Scan(stream, "png-only", Path.Combine(work, "png-only"), null, null,
            default, options: new(Jpeg: false));
        Check(onlyPng.Files.Count == 2 && onlyPng.Files.All(f => f.FileType == "PNG"), "PNG-only scan excludes JPEG");
    }
    using (var stream = new MemoryStream(catalogueData))
    {
        var onlyJpeg = Carver.Scan(stream, "jpeg-only", Path.Combine(work, "jpeg-only"), null, null,
            default, options: new(Png: false));
        Check(onlyJpeg.Files.Count == 1 && onlyJpeg.Files[0].FileType == "JPG", "JPEG-only scan excludes PNG");
    }
    using (var stream = new MemoryStream(catalogueData))
    {
        bool rejected = false;
        try { Carver.Scan(stream, "no-type", Path.Combine(work, "no-type"), null, null, default, options: new(false, false)); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected && !Directory.Exists(Path.Combine(work, "no-type")), "No-type scan rejected before output creation");
    }
    string changedFolder = Path.Combine(work, "changed-source");
    using (var stream = new MemoryStream(catalogueData))
    {
        var catalogue = Carver.Scan(stream, "changed", changedFolder, null, null, default, options: new(SaveAll: false));
        byte[] altered = (byte[])catalogueData.Clone(); altered[45] ^= 1;
        using var changed = new MemoryStream(altered);
        bool rejected = false;
        try { ResultFiles.SaveSelected(changed, changedFolder, [catalogue.Files[0]], default); }
        catch (IOException) { rejected = true; }
        Check(rejected, "Changed source detected by hash before committing export");
        Check(Directory.GetFiles(changedFolder, "*.png").Length == 0 && Directory.GetFiles(changedFolder, "*.part").Length == 0,
            "Hash-mismatched image and partial file are discarded");
        Check(ResultFiles.LoadReport(changedFolder).Files.All(f => !f.IsSaved), "Changed source does not mark an image saved");
        var recovered = ResultFiles.SaveSelected(changed, changedFolder, [catalogue.Files[0]], default, allowChanged: true).Files[0];
        byte[] savedBytes = File.ReadAllBytes(Path.Combine(changedFolder, recovered.FileName));
        Check(recovered.IsSaved && recovered.SourceChanged, "Changed image saved and flagged when allowed");
        Check(savedBytes.SequenceEqual(altered.Skip((int)recovered.Offset).Take((int)recovered.Bytes)), "Changed export preserves exact current bytes");
        Check(recovered.ScannedSha256 == catalogue.Files[0].Sha256 && recovered.Sha256 == Convert.ToHexString(SHA256.HashData(savedBytes)), "Report retains scanned and actual saved hashes");
        Check(ResultFiles.LoadReport(changedFolder).Files[0] == recovered, "Changed state persists in session report");
    }
    string batchFolder = Path.Combine(work, "batch");
    var batchReport = Run(catalogueData, "batch");
    var batch = ResultFiles.RenameBatch(batchFolder, batchReport.Files, "Trip");
    Check(batch.Select(f => f.FileName).SequenceEqual(new[] { "Trip_001.png", "Trip_002.jpg", "Trip_003.png" }), "Sequential batch names follow selected order");
    Check(batch.All(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(batchFolder, f.FileName)))) == f.Sha256),
        "Batch rename preserves all image bytes");
    Check(ResultFiles.LoadReport(batchFolder).Files.Select(f => f.FileName).SequenceEqual(batch.Select(f => f.FileName)), "Batch rename updates all session names");
    var reversed = ResultFiles.RenameBatch(batchFolder, batch.AsEnumerable().Reverse().ToList(), "Trip");
    Check(reversed.All(f => File.Exists(Path.Combine(batchFolder, f.FileName))), "Batch rename handles name cycles with temporary staging");
    File.WriteAllText(Path.Combine(batchFolder, "Blocked_002.jpg"), "keep");
    bool batchCollision = false;
    try { ResultFiles.RenameBatch(batchFolder, reversed.AsEnumerable().Reverse().ToList(), "Blocked"); }
    catch (IOException) { batchCollision = true; }
    Check(batchCollision && reversed.All(f => File.Exists(Path.Combine(batchFolder, f.FileName))), "Batch collision rejected before moving any files");
    Check(File.ReadAllText(Path.Combine(batchFolder, "Blocked_002.jpg")) == "keep", "Batch collision never overwrites other data");

    string settingsPath = Path.Combine(work, "settings", "settings.json");
    var preferences = new Preferences("zh-Hans", "soft", @"C:\Pictures\اختبار");
    Check(PreferenceStore.Save(preferences, settingsPath), "Preferences saved atomically");
    Check(PreferenceStore.Load(settingsPath) == preferences, "Language, appearance and Unicode destination persist");
    File.WriteAllText(settingsPath, "{broken");
    Check(PreferenceStore.Load(settingsPath) == new Preferences(), "Corrupt preferences fall back to defaults");
    File.WriteAllText(settingsPath, "{\"Language\":\"invalid\",\"Theme\":\"invalid\",\"Destination\":null}");
    Check(PreferenceStore.Load(settingsPath) == new Preferences(), "Unsupported stored preferences normalized");
    Check(PreferenceStore.Load(settingsPath + ".missing") == new Preferences(), "Missing preferences handled");
    var localeFiles = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Languages"), "*.json");
    Check(localeFiles.Length == 10, "Ten language files delivered");
    var english = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(localeFiles.Single(f => Path.GetFileName(f) == "en.json")))!;
    foreach (var locale in localeFiles)
    {
        var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(locale))!;
        Check(dictionary.Keys.Order().SequenceEqual(english.Keys.Order()) && dictionary.Values.All(v => !string.IsNullOrWhiteSpace(v)),
            "Complete localization: " + Path.GetFileName(locale));
    }
    foreach (byte[] malformed in new[] { Array.Empty<byte>(), new byte[2], new byte[16], new byte[] { 73, 73, 42, 0, 255, 255, 255, 255 } })
        Check(PhotoMetadata.ParseTiff(malformed) == new PhotoInfo(), "Malformed TIFF metadata handled");
    using (var pause = new PauseController())
    {
        pause.Pause(); Thread.Sleep(30); double beforeResume = pause.PausedSeconds; pause.Resume();
        Check(beforeResume > 0 && pause.PausedSeconds >= beforeResume, "Paused duration tracked for speed and ETA");
        double stopped = pause.PausedSeconds; Thread.Sleep(15);
        Check(pause.PausedSeconds == stopped, "Active scan time does not accumulate as paused time");
    }
    string partialFolder = Path.Combine(work, "partial-selected");
    using (var stream = new MemoryStream(catalogueData))
    using (var cancel = new CancellationTokenSource())
    {
        var catalogue = Carver.Scan(stream, "partial-selected", partialFolder, null, null,
            default, options: new(SaveAll: false));
        bool cancelled = false;
        try
        {
            ResultFiles.SaveSelected(stream, partialFolder, catalogue.Files, cancel.Token,
                new InlineProgress<RecoveredImage>(_ => cancel.Cancel()));
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Selective export responds to cancellation between images");
        Check(ResultFiles.LoadReport(partialFolder).Files.Count(f => f.IsSaved) == 1,
            "Cancelled selective export records the one completed image");
        Check(Directory.GetFiles(partialFolder, "*.png").Length == 1 && Directory.GetFiles(partialFolder, "*.jpg").Length == 0,
            "Cancelled selective export keeps only completed output");
        Check(Directory.GetFiles(partialFolder, "*.part").Length == 0, "Cancelled selective export leaves no partial files");
    }
    byte[] littleExif = new byte[100];
    littleExif[0] = littleExif[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(littleExif.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32LittleEndian(littleExif.AsSpan(4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(littleExif.AsSpan(8), 3);
    LittleEntry(10, 0x010F, 2, 5, 70);
    LittleEntry(22, 0x0110, 2, 4, 0);
    "Cam\0"u8.CopyTo(littleExif.AsSpan(30));
    LittleEntry(34, 0x8769, 4, 1, 50);
    BinaryPrimitives.WriteUInt16LittleEndian(littleExif.AsSpan(50), 1);
    LittleEntry(52, 0x9003, 2, 20, 80);
    "Acme\0"u8.CopyTo(littleExif.AsSpan(70));
    "2026:10:22 01:02:03\0"u8.CopyTo(littleExif.AsSpan(80));
    var littleInfo = PhotoMetadata.ParseTiff(littleExif);
    Check(littleInfo.Camera == "Acme Cam", "Little-endian TIFF handles inline and offset camera strings");
    Check(littleInfo.DateTaken == "2026-10-22 01:02:03", "Little-endian EXIF sub-IFD capture date read correctly");
    void LittleEntry(int p, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(littleExif.AsSpan(p), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(littleExif.AsSpan(p + 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(littleExif.AsSpan(p + 4), count);
        BinaryPrimitives.WriteUInt32LittleEndian(littleExif.AsSpan(p + 8), value);
    }
    AdditionalFormatTests.Run(work, Check);
    Console.WriteLine($"PASS: {checks} checks");
}
finally { Directory.Delete(work, true); }

ScanReport Run(byte[] data, string name, CancellationToken token = default)
{
    using var stream = new MemoryStream(data);
    return Carver.Scan(stream, name, Path.Combine(work, name), null, null, token);
}
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
static byte[] MakePng()
{
    using var output = new MemoryStream();
    output.Write(new byte[] {137,80,78,71,13,10,26,10});
    byte[] ihdr = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1);
    BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 1);
    ihdr[8] = 8; ihdr[9] = 2;
    Chunk("IHDR", ihdr);
    using var compressed = new MemoryStream();
    using (var z = new ZLibStream(compressed, CompressionMode.Compress, true)) z.Write(new byte[] {0,255,0,0});
    Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
    return output.ToArray();
    void Chunk(string type, byte[] bytes)
    {
        Span<byte> n = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(n, (uint)bytes.Length); output.Write(n);
        byte[] data = [..System.Text.Encoding.ASCII.GetBytes(type), ..bytes];
        output.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(n, crc ^ 0xFFFFFFFF); output.Write(n);
    }
}
sealed class ChunkedStream(byte[] data) : MemoryStream(data)
{
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(17, count));
}
sealed class BrokenStream(byte[] data) : MemoryStream(data)
{
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Synthetic media error");
}
sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
