using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ImageRescue.App.Recovery;

public sealed record RecoveredImage(string FileName, string FileType, long Bytes,
    long Offset, string Sha256, int Width = 0, int Height = 0)
{
    public string FileSize => $"{Bytes:N0} B ({Bytes / 1048576d:N2} MiB)";
    public string Dimensions => $"{Width} × {Height}";
}

public enum ScanMode { Quick, Deep }

public sealed class PauseController : IDisposable
{
    private readonly ManualResetEventSlim _gate = new(true);
    public Action? Paused { get; set; }
    public bool IsPauseRequested => !_gate.IsSet;
    public void Pause() => _gate.Reset();
    public void Resume() => _gate.Set();
    public void Checkpoint(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_gate.IsSet) return;
        Paused?.Invoke();
        _gate.Wait(token);
        token.ThrowIfCancellationRequested();
    }
    public void Dispose() => _gate.Dispose();
}

public sealed record ScanUpdate(double Percent, long Scanned, int Found);
public sealed record ScanReport(string Source, long SourceBytes, string Scope,
    DateTime StartedUtc, DateTime FinishedUtc, string Outcome, long NextOffset,
    List<RecoveredImage> Files, string Mode = "Deep");

// The scanner is independent of WPF and can be tested with ordinary streams.
public static class Carver
{
    public const long MaxImageBytes = 64L * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = MakeCrcTable();

    public static ScanReport Scan(Stream source, string sourceName, string destination,
        IProgress<ScanUpdate>? progress, IProgress<RecoveredImage>? found,
        CancellationToken cancellation, ScanMode mode = ScanMode.Deep,
        PauseController? pause = null, string namePrefix = "image")
    {
        ResultFiles.ValidateBaseName(namePrefix);
        Directory.CreateDirectory(destination);
        var started = DateTime.UtcNow;
        var files = new List<RecoveredImage>();
        var reader = new CachedReader(source, cancellation, pause);
        var scanBuffer = new byte[1024 * 1024 + 7];
        long position = 0;
        string outcome = "Completed";
        var watch = Stopwatch.StartNew();
        Exception? failure = null;
        try
        {
            while (position < source.Length)
            {
                Checkpoint();
                source.Position = position;
                int wanted = (int)Math.Min(scanBuffer.Length, source.Length - position);
                int count = ReadSome(source, scanBuffer, wanted);
                if (count == 0) throw new EndOfStreamException("Unexpected end of source.");

                // Seven bytes overlap protects PNG signatures crossing a scan block.
                int step = Math.Min(1024 * 1024, count);
                long next = position + step;
                int stride = mode == ScanMode.Quick ? 512 : 1;
                int initial = (int)((stride - position % stride) % stride);
                for (int i = initial; i < step; i += stride)
                {
                    if ((i & 4095) == 0) Checkpoint();
                    bool png = i + 8 <= count &&
                        scanBuffer.AsSpan(i, 8).SequenceEqual(PngSignature);
                    bool jpg = i + 3 <= count && scanBuffer[i] == 255 &&
                        scanBuffer[i + 1] == 216 && scanBuffer[i + 2] == 255;
                    if (!png && !jpg) continue;

                    long start = position + i;
                    long end = png ? PngEnd(reader, start) : JpegEnd(reader, start);
                    if (end <= start) continue;

                    string extension = png ? "png" : "jpg";
                    string name = $"{namePrefix}_{start:X16}.{extension}";
                    var dimensions = Dimensions(reader, start, png);
                    string hash = Export(source, start, end - start,
                        Path.Combine(destination, name), cancellation, pause);
                    var item = new RecoveredImage(name, extension.ToUpperInvariant(),
                        end - start, start, hash, dimensions.Width, dimensions.Height);
                    files.Add(item);
                    found?.Report(item);
                    if (files.Count >= 10000)
                    {
                        outcome = "ResultLimitReached";
                        position = end;
                        return Finish();
                    }

                    // An image was verified structurally: do not carve its metadata thumbnails again.
                    if (end >= next)
                    {
                        next = end;
                        break;
                    }
                    long alignedEnd = ((end + stride - 1) / stride) * stride;
                    i = (int)(alignedEnd - position) - stride;
                }
                position = next;
                if (watch.ElapsedMilliseconds >= 150)
                {
                    progress?.Report(new(100d * position / Math.Max(1, source.Length),
                        position, files.Count));
                    watch.Restart();
                }
            }
        }
        catch (OperationCanceledException) { outcome = "Cancelled"; }
        catch (Exception ex) { outcome = "Failed"; failure = ex; }

        var report = Finish();
        if (failure is not null)
            throw new IOException("Scan stopped; completed files and the session report were kept.", failure);
        return report;

        ScanReport Finish()
        {
            var result = new ScanReport(sourceName, source.Length,
                "Signature carving: existing and deleted contiguous images; not deleted-only. " +
                "Structure checked, not a guarantee of pixel integrity. Maximum image: 64 MiB.",
                started, DateTime.UtcNow, outcome, position, files, mode.ToString());
            string reportPath = Path.Combine(destination, "scan-session.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }));
            progress?.Report(new(outcome == "Completed" ? 100 :
                Math.Min(100, 100d * position / Math.Max(1, source.Length)), position, files.Count));
            return result;
        }

        void Checkpoint()
        {
            if (pause is not null) pause.Checkpoint(cancellation);
            else cancellation.ThrowIfCancellationRequested();
        }
    }

    private static (int Width, int Height) Dimensions(CachedReader reader, long start, bool png)
    {
        if (png) return ((int)reader.UInt32(start + 16), (int)reader.UInt32(start + 20));
        long p = start + 2;
        while (p + 4 < reader.Length)
        {
            reader.CheckCancellation();
            if (reader.Byte(p++) != 255) break;
            while (reader.Byte(p) == 255) p++;
            int marker = reader.Byte(p++);
            if (marker is 0xC0 or 0xC1 or 0xC2)
                return ((reader.Byte(p + 5) << 8) | reader.Byte(p + 6),
                    (reader.Byte(p + 3) << 8) | reader.Byte(p + 4));
            if (marker == 1) continue;
            int size = (reader.Byte(p) << 8) | reader.Byte(p + 1);
            if (size < 2) break;
            p += size;
        }
        return (0, 0);
    }

    public static long PngEnd(CachedReader reader, long start)
    {
        long limit = Math.Min(reader.Length, start + MaxImageBytes);
        long pos = start + 8;
        bool first = true, idat = false;
        bool palette = false;
        int colorType = -1;
        var work = new byte[16384];
        while (pos + 12 <= limit)
        {
            reader.CheckCancellation();
            uint size = reader.UInt32(pos);
            uint type = reader.UInt32(pos + 4);
            long end = pos + 12L + size;
            if (end > limit) return -1;
            for (int i = 0; i < 4; i++)
            {
                int c = reader.Byte(pos + 4 + i);
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')) return -1;
            }
            if ((reader.Byte(pos + 6) & 32) != 0) return -1;
            if (first)
            {
                if (type != 0x49484452 || size != 13) return -1; // IHDR
                uint w = reader.UInt32(pos + 8), h = reader.UInt32(pos + 12);
                if (w == 0 || h == 0 || (ulong)w * h > 40_000_000) return -1;
                int depth = reader.Byte(pos + 16);
                colorType = reader.Byte(pos + 17);
                bool depthOk = colorType switch
                {
                    0 => depth is 1 or 2 or 4 or 8 or 16,
                    2 or 4 or 6 => depth is 8 or 16,
                    3 => depth is 1 or 2 or 4 or 8,
                    _ => false
                };
                if (!depthOk || reader.Byte(pos + 18) != 0 ||
                    reader.Byte(pos + 19) != 0 || reader.Byte(pos + 20) > 1) return -1;
            }
            else if (type == 0x49484452) return -1;
            if (type == 0x504C5445)
            {
                if (idat || palette || size == 0 || size > 768 || size % 3 != 0) return -1;
                palette = true;
            }
            // Unknown critical chunks cannot be safely interpreted.
            if ((reader.Byte(pos + 4) & 32) == 0 &&
                type is not (0x49484452 or 0x504C5445 or 0x49444154 or 0x49454E44)) return -1;
            uint crc = 0xFFFFFFFF;
            long cursor = pos + 4;
            long remaining = size + 4L;
            while (remaining > 0)
            {
                int n = (int)Math.Min(work.Length, remaining);
                if (!reader.Copy(cursor, work, n)) return -1;
                for (int i = 0; i < n; i++) crc = CrcTable[(crc ^ work[i]) & 255] ^ (crc >> 8);
                cursor += n;
                remaining -= n;
            }
            if ((crc ^ 0xFFFFFFFF) != reader.UInt32(pos + 8L + size)) return -1;
            if (type == 0x49444154)
            {
                if (colorType == 3 && !palette) return -1;
                idat = true;
            }
            if (type == 0x49454E44) return size == 0 && idat ? end : -1;
            first = false;
            pos = end;
        }
        return -1;
    }

    public static long JpegEnd(CachedReader reader, long start)
    {
        long pos = start + 2;
        long limit = Math.Min(reader.Length, start + MaxImageBytes);
        bool frame = false, scan = false, entropy = false;
        while (pos < limit)
        {
            reader.CheckCancellation();
            int marker;
            if (entropy)
            {
                while (pos < limit && reader.Byte(pos) != 255) pos++;
                if (pos >= limit) return -1;
                pos++;
                while (pos < limit && reader.Byte(pos) == 255) pos++;
                if (pos >= limit) return -1;
                marker = reader.Byte(pos++);
                if (marker == 0 || marker is >= 0xD0 and <= 0xD7) continue;
                entropy = false;
            }
            else
            {
                if (reader.Byte(pos++) != 255) return -1;
                while (pos < limit && reader.Byte(pos) == 255) pos++;
                if (pos >= limit) return -1;
                marker = reader.Byte(pos++);
            }
            if (marker == 0xD9) return frame && scan ? pos : -1;
            if (marker is 0 or 0xD8 || marker is >= 0xD0 and <= 0xD7) return -1;
            if (marker == 1) continue;
            if (pos + 2 > limit) return -1;
            int size = (reader.Byte(pos) << 8) | reader.Byte(pos + 1);
            if (size < 2 || pos + size > limit) return -1;
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (size < 11) return -1;
                int h = (reader.Byte(pos + 3) << 8) | reader.Byte(pos + 4);
                int w = (reader.Byte(pos + 5) << 8) | reader.Byte(pos + 6);
                int components = reader.Byte(pos + 7);
                if (h == 0 || w == 0 || (long)h * w > 40_000_000 ||
                    components is < 1 or > 4 || size != 8 + 3 * components) return -1;
                frame = true;
            }
            if (marker == 0xDA)
            {
                if (!frame || size < 6) return -1;
                int components = reader.Byte(pos + 2);
                if (components is < 1 or > 4 || size != 6 + 2 * components) return -1;
                scan = true;
                entropy = true;
            }
            pos += size;
        }
        return -1;
    }

    private static string Export(Stream source, long start, long size, string path,
        CancellationToken cancellation, PauseController? pause)
    {
        string temporary = path + ".part";
        bool created = false;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                created = true;
                source.Position = start;
                var buffer = new byte[1024 * 1024];
                long remaining = size;
                while (remaining > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    pause?.Checkpoint(cancellation);
                    int n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (n == 0) throw new EndOfStreamException();
                    output.Write(buffer, 0, n);
                    hash.AppendData(buffer, 0, n);
                    remaining -= n;
                }
            }
            File.Move(temporary, path, false);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static int ReadSome(Stream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buffer, total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}

public sealed class CachedReader(Stream stream, CancellationToken cancellation, PauseController? pause = null)
{
    private readonly byte[] _buffer = new byte[1024 * 1024];
    private long _start = -1;
    private int _count;
    public long Length => stream.Length;
    public void CheckCancellation()
    {
        cancellation.ThrowIfCancellationRequested();
        pause?.Checkpoint(cancellation);
    }

    public int Byte(long offset)
    {
        if (offset < 0 || offset >= Length) return -1;
        if (_start < 0 || offset < _start || offset >= _start + _count)
        {
            CheckCancellation();
            _start = offset / _buffer.Length * _buffer.Length;
            stream.Position = _start;
            int wanted = (int)Math.Min(_buffer.Length, Length - _start);
            _count = 0;
            while (_count < wanted)
            {
                int n = stream.Read(_buffer, _count, wanted - _count);
                if (n == 0) throw new EndOfStreamException();
                _count += n;
            }
        }
        return _buffer[(int)(offset - _start)];
    }

    public uint UInt32(long offset)
    {
        Span<byte> bytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) bytes[i] = (byte)Byte(offset + i);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    public bool Copy(long offset, byte[] destination, int count)
    {
        if (offset < 0 || offset + count > Length) return false;
        for (int i = 0; i < count; i++) destination[i] = (byte)Byte(offset + i);
        return true;
    }
}
