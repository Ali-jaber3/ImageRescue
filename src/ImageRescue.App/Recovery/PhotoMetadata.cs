using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ImageRescue.App.Recovery;

public sealed record PhotoInfo(string? Camera = null, string? DateTaken = null);

// Bounded EXIF reader. No image decoding, re-encoding, network calls or source writes.
public static class PhotoMetadata
{
    public static PhotoInfo Read(CachedReader reader, long start, long end, bool png)
    {
        if (png)
        {
            long p = start + 8;
            while (p + 12 <= end)
            {
                reader.CheckCancellation();
                uint size = reader.UInt32(p);
                if (size > end - p - 12) break;
                if (reader.UInt32(p + 4) == 0x65584966 && size <= 1_048_576)
                {
                    var data = new byte[(int)size];
                    if (reader.Copy(p + 8, data, data.Length)) return ParseTiff(data);
                }
                p += size + 12L;
            }
            return new();
        }
        long pos = start + 2;
        while (pos + 4 < end)
        {
            reader.CheckCancellation();
            if (reader.Byte(pos++) != 255) break;
            while (pos < end && reader.Byte(pos) == 255) pos++;
            int marker = reader.Byte(pos++);
            if (marker is 0xDA or 0xD9) break;
            if (marker == 1) continue;
            int size = (reader.Byte(pos) << 8) | reader.Byte(pos + 1);
            if (size < 2 || pos + size > end) break;
            if (marker == 0xE1 && size >= 8)
            {
                var data = new byte[size - 2];
                if (reader.Copy(pos + 2, data, data.Length) && data.AsSpan(0, 6).SequenceEqual("Exif\0\0"u8))
                    return ParseTiff(data[6..]);
            }
            pos += size;
        }
        return new();
    }

    public static PhotoInfo ParseTiff(byte[] data)
    {
        if (data.Length < 8) return new();
        bool little = data[0] == 'I' && data[1] == 'I';
        if (!little && !(data[0] == 'M' && data[1] == 'M')) return new();
        ushort U16(int p) => little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p, 2));
        uint U32(int p) => little ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p, 4));
        if (U16(2) != 42) return new();
        string? make = null, model = null, original = null, digitized = null;
        var visited = new HashSet<uint>();
        Walk(U32(4), 0);
        string? camera = string.Join(" ", new[] { make, model }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (model is not null && make is not null && model.StartsWith(make, StringComparison.OrdinalIgnoreCase)) camera = model;
        if (string.IsNullOrWhiteSpace(camera)) camera = null;
        string? rawDate = original ?? digitized;
        string? date = rawDate is not null && DateTime.TryParseExact(rawDate,
            "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken)
            ? taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null;
        return new(camera, date);

        void Walk(uint offset, int depth)
        {
            if (depth > 3 || !visited.Add(offset) || offset > data.Length - 2) return;
            int p = (int)offset;
            int count = U16(p);
            if (count > 256 || p + 2L + 12L * count > data.Length) return;
            for (int i = 0; i < count; i++)
            {
                int entry = p + 2 + 12 * i;
                ushort tag = U16(entry), type = U16(entry + 2);
                uint length = U32(entry + 4);
                if (tag == 0x8769 && type == 4 && length == 1) Walk(U32(entry + 8), depth + 1);
                if (type != 2 || length == 0 || length > 8192) continue;
                uint begin = length <= 4 ? (uint)(entry + 8) : U32(entry + 8);
                if ((ulong)begin + length > (ulong)data.Length) continue;
                string text = Encoding.ASCII.GetString(data, (int)begin, (int)length).TrimEnd('\0', ' ');
                text = new string(text.Where(c => !char.IsControl(c)).ToArray());
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (tag == 0x010F) make ??= text;
                if (tag == 0x0110) model ??= text;
                if (tag == 0x9003) original ??= text;
                if (tag == 0x9004) digitized ??= text;
            }
        }
    }
}
