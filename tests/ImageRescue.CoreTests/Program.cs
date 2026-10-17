using ImageRescue.App.Recovery;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using System.IO.Compression;

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
    }
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
