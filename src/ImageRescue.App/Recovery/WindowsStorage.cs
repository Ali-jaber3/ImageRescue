using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ImageRescue.App.Recovery;

public sealed record StorageTarget(string Root, string Label, long Size)
{
    public override string ToString() => $"{Root}  {Label}  — {Size / 1073741824d:N1} GB";
}

public static class WindowsStorage
{
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

    public static List<StorageTarget> ListTargets()
    {
        var targets = new List<StorageTarget>();
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.Name.Equals(systemRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed)) continue;
                using var handle = OpenVolume(drive.Name, false);
                int bus = BusType(handle);
                if (drive.DriveType != DriveType.Removable && bus is not (7 or 12 or 13)) continue;
                targets.Add(new(drive.Name, drive.VolumeLabel, drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            { /* Inaccessible and unready volumes are not offered. */ }
        }
        return targets;
    }

    public static RawVolumeStream OpenSource(StorageTarget target, string destination)
    {
        if (!IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        // Recheck immediately before opening: drive letters can change after device removal.
        var current = ListTargets().FirstOrDefault(t => t.Root == target.Root);
        if (current is null || current.Size != target.Size || current.Label != target.Label)
            throw new IOException("The selected device changed. Refresh and select it again.");
        EnsureSeparateDisks(target.Root, destination, true);
        return new RawVolumeStream(target.Root);
    }

    public static void ValidateImageDestination(string image, string destination)
    {
        // For an IMG source, output must not reside on its physical disk either.
        EnsureSeparateDisks(ResolveVolumeRoot(image), destination, false);
    }

    private static void EnsureSeparateDisks(string sourceRoot, string destination, bool protectSystem)
    {
        string outputRoot = ResolveVolumeRoot(destination);
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        if (sourceRoot.StartsWith("\\\\") || outputRoot.StartsWith("\\\\"))
            throw new IOException("Network paths are not supported in this version.");
        using var source = OpenVolume(sourceRoot, false);
        using var output = OpenVolume(outputRoot, false);
        var sourceDisks = DiskNumbers(source);
        var outputDisks = DiskNumbers(output);
        if (sourceDisks.Intersect(outputDisks).Any())
            throw new IOException("Choose a destination on a different physical disk.");
        // Protect the running Windows disk for direct-device scans.
        // IMG files are allowed on it because only the ordinary IMG file is read.
        if (protectSystem)
        {
            using var system = OpenVolume(systemRoot, false);
            if (sourceDisks.Intersect(DiskNumbers(system)).Any())
                throw new IOException("The selected volume shares the Windows system disk.");
        }
    }

    // Resolve junctions/symlinks before checking disk identity. Refuse unusual mounted
    // volume layouts rather than accidentally writing recovered data to the source.
    private static string ResolveVolumeRoot(string path)
    {
        using var handle = Native.CreateFileW(Path.GetFullPath(path), 0, 7,
            IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var final = new StringBuilder(32768);
        uint length = Native.GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
        if (length == 0 || length >= final.Capacity)
            throw new IOException("Cannot resolve storage location.");
        string actual = final.ToString();
        if (actual.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Network destinations are not supported.");
        if (actual.StartsWith("\\\\?\\")) actual = actual[4..];
        var volume = new StringBuilder(32768);
        if (!Native.GetVolumePathNameW(actual, volume, volume.Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        string root = volume.ToString();
        if (root.Length != 3 || root[1] != ':')
            throw new IOException("Use a normal drive-letter volume, not a folder mount point.");
        return root;
    }

    internal static SafeFileHandle OpenVolume(string root, bool read)
    {
        if (root.Length < 2 || root[1] != ':' || !char.IsAsciiLetter(root[0]))
            throw new IOException("A local drive letter is required.");
        var handle = Native.CreateFileW("\\\\.\\" + root[..2],
            read ? 0x80000000u : 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    private static int BusType(SafeFileHandle handle)
    {
        byte[] query = new byte[12], result = new byte[1024];
        return Native.DeviceIoControl(handle, 0x002D1400, query, query.Length,
            result, result.Length, out int received, IntPtr.Zero) && received >= 32
            ? BitConverter.ToInt32(result, 28) : -1;
    }

    private static HashSet<uint> DiskNumbers(SafeFileHandle handle)
    {
        var result = new byte[8192];
        if (!Native.DeviceIoControl(handle, 0x00560000, null, 0,
                result, result.Length, out int received, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot verify physical disk separation.");
        int count = BitConverter.ToInt32(result, 0);
        // DISK_EXTENT is 24 bytes; first extent is aligned to 8 bytes on x64.
        if (count < 1 || 8L + count * 24L > received)
            throw new IOException("Unsupported disk extent layout.");
        var numbers = new HashSet<uint>();
        for (int i = 0; i < count; i++) numbers.Add(BitConverter.ToUInt32(result, 8 + 24 * i));
        return numbers;
    }
}

// Native reads use VirtualAlloc memory and whole sectors. This avoids the alignment
// problems that ordinary managed buffers can cause when reading a raw Windows volume.
public sealed class RawVolumeStream : Stream
{
    private readonly SafeFileHandle _handle;
    private IntPtr _memory;
    private readonly int _sector;
    private long _position;
    private readonly long _length;
    private const int BufferSize = 1024 * 1024;

    public RawVolumeStream(string root)
    {
        _handle = WindowsStorage.OpenVolume(root, true);
        try
        {
            if (!Native.GetDiskFreeSpaceW(root, out _, out uint bytes, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _sector = checked((int)bytes);
            if (_sector is < 512 or > 65536 || BufferSize % _sector != 0)
                throw new IOException("Unsupported sector size.");
            var length = new byte[8];
            if (!Native.DeviceIoControl(_handle, 0x0007405C, null, 0,
                    length, 8, out int received, IntPtr.Zero) || received != 8)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _length = BitConverter.ToInt64(length, 0);
            if (_length <= 0 || _length % _sector != 0)
                throw new IOException("Unsupported volume length.");
            _memory = Native.VirtualAlloc(IntPtr.Zero, (nuint)BufferSize, 0x3000, 4);
            if (_memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { _handle.Dispose(); throw; }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException();
        if (_position >= _length || count == 0) return 0;
        long aligned = _position / _sector * _sector;
        int skip = (int)(_position - aligned);
        int wanted = (int)Math.Min(Math.Min(count, BufferSize - skip), _length - _position);
        int nativeCount = ((wanted + skip + _sector - 1) / _sector) * _sector;
        if (!Native.SetFilePointerEx(_handle, aligned, out _, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!Native.ReadFile(_handle, _memory, nativeCount, out int actual, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw read failed. Scan stopped.");
        int copied = Math.Min(wanted, actual - skip);
        if (copied <= 0) throw new EndOfStreamException();
        Marshal.Copy(IntPtr.Add(_memory, skip), buffer, offset, copied);
        _position += copied;
        return copied;
    }

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _length) throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }
    public override long Length => _length;
    public override bool CanRead => !_handle.IsClosed;
    public override bool CanSeek => !_handle.IsClosed;
    public override bool CanWrite => false;
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return Position;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) _handle.Dispose();
        if (_memory != IntPtr.Zero)
        {
            Native.VirtualFree(_memory, 0, 0x8000);
            _memory = IntPtr.Zero;
        }
        base.Dispose(disposing);
    }
}

internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle,
        StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumePathNameW(string path, StringBuilder volume, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string name, uint access,
        uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
        byte[]? input, int inputSize, byte[] output, int outputSize,
        out int received, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetDiskFreeSpaceW(string root, out uint sectors,
        out uint bytes, out uint free, out uint total);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFilePointerEx(SafeFileHandle handle, long distance,
        out long position, uint method);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer,
        int count, out int read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFree(IntPtr address, nuint size, uint type);
}
