#if LINUX
using System.IO.Compression;
using System.Runtime.InteropServices;
using AdagioMachineAgent.Models;
using Tmds.DBus.Protocol;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Linux implementation of <see cref="IUiAutomationService"/> using the AT-SPI2
/// accessibility stack (via D-Bus) for UI automation and the X11 API for screenshots.
/// </summary>
/// <remarks>
/// Prerequisites on the Linux host:
/// <list type="bullet">
///   <item><description>A running D-Bus session bus (<c>DBUS_SESSION_BUS_ADDRESS</c> set).</description></item>
///   <item><description>AT-SPI2 daemon active (<c>at-spi2-core</c> package).</description></item>
///   <item><description>Target application launched with AT-SPI2 accessibility enabled.</description></item>
///   <item><description><c>libX11.so.6</c> available (standard on all X11 desktops).</description></item>
/// </list>
/// </remarks>
public sealed class LinuxUiAutomationService : IUiAutomationService
{
    // ── AT-SPI2 / D-Bus constants ─────────────────────────────────────────────

    private const string AtSpiRegistryService = "org.a11y.atspi.Registry";
    private const string AtSpiRegistryPath    = "/org/a11y/atspi/registry";
    private const string AtSpiAccessible      = "org.a11y.atspi.Accessible";
    private const string AtSpiComponent       = "org.a11y.atspi.Component";
    private const string AtSpiAction          = "org.a11y.atspi.Action";
    private const string AtSpiEditableText    = "org.a11y.atspi.EditableText";
    private const string DBusBusService       = "org.freedesktop.DBus";
    private const string DBusBusPath          = "/org/freedesktop/DBus";
    private const string DBusProperties       = "org.freedesktop.DBus.Properties";

    // ── State ─────────────────────────────────────────────────────────────────

    private DBusConnection? _connection;
    private bool _disposed;

    // ── IUiAutomationService ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public UiTreeResponse GetUiTree(int pid)
    {
        var conn = GetConnection();
        var (appBus, appPath) = FindApplicationByPid(conn, pid);

        // Children of the application accessible are its top-level windows.
        var windows = GetAccessibleChildren(conn, appBus, appPath);
        if (windows.Length == 0)
            throw new InvalidOperationException(
                $"No accessible windows found for process {pid}.");

        var (winBus, winPath) = windows[0];
        var winTitle = GetStringProperty(conn, winBus, winPath, AtSpiAccessible, "Name");
        var elements = WalkChildren(conn, winBus, winPath);
        return new UiTreeResponse(winTitle, elements);
    }

    /// <inheritdoc/>
    public void Click(int pid, string elementId)
    {
        var conn = GetConnection();
        var (elBus, elPath) = FindElementByPidAndId(conn, pid, elementId);
        DoAction(conn, elBus, elPath, 0); // action 0 is typically "click" / "press"
    }

    /// <inheritdoc/>
    public void Type(int pid, string elementId, string text)
    {
        var conn = GetConnection();
        var (elBus, elPath) = FindElementByPidAndId(conn, pid, elementId);
        GrabFocus(conn, elBus, elPath);
        SetTextContents(conn, elBus, elPath, text);
    }

    /// <inheritdoc/>
    public void SetFocus(int pid, string elementId)
    {
        var conn = GetConnection();
        var (elBus, elPath) = FindElementByPidAndId(conn, pid, elementId);
        GrabFocus(conn, elBus, elPath);
    }

    /// <inheritdoc/>
    public void SendKeys(int pid, string text)
    {
        throw new PlatformNotSupportedException(
            "Generic send-keys is not implemented on Linux yet. Use Type on a focused editable element instead.");
    }

    /// <inheritdoc/>
    public void PressHotkey(int pid, IReadOnlyList<string> keys)
    {
        throw new PlatformNotSupportedException(
            "Generic hotkey simulation is not implemented on Linux yet.");
    }

    /// <inheritdoc/>
    public ElementStateResponse GetElementState(int pid, string elementId)
    {
        var conn = GetConnection();
        var (elBus, elPath) = FindElementByPidAndId(conn, pid, elementId);
        return ToElementState(conn, elBus, elPath);
    }

    /// <inheritdoc/>
    public WaitForElementResponse WaitForElement(int pid, string elementId, int timeoutMilliseconds, int pollIntervalMilliseconds)
    {
        var conn = GetConnection();
        var startedAt = DateTime.UtcNow;

        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMilliseconds)
        {
            var match = TryFindElementByPidAndId(conn, pid, elementId);
            if (match is not null)
            {
                var (elBus, elPath) = match.Value;
                return new WaitForElementResponse(true, ToElementState(conn, elBus, elPath));
            }

            Thread.Sleep(pollIntervalMilliseconds);
        }

        return new WaitForElementResponse(false, null);
    }

    /// <inheritdoc/>
    public string CaptureScreenshot(int pid)
    {
        var conn = GetConnection();
        var (appBus, appPath) = FindApplicationByPid(conn, pid);

        var windows = GetAccessibleChildren(conn, appBus, appPath);
        if (windows.Length == 0)
            throw new InvalidOperationException(
                $"No accessible windows found for process {pid}.");

        var (winBus, winPath) = windows[0];
        var (x, y, width, height) = GetExtents(conn, winBus, winPath, coordType: 0);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                $"Window for process {pid} has invalid bounds ({width}×{height}).");

        return CaptureScreenRegion(x, y, width, height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection?.Dispose();
        _connection = null;
    }

    // ── DBusConnection ────────────────────────────────────────────────────────────

    private DBusConnection GetConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is not null) return _connection;

        var sessionAddress = DBusAddress.Session;
        if (string.IsNullOrEmpty(sessionAddress))
            throw new PlatformNotSupportedException(
                "DBUS_SESSION_BUS_ADDRESS is not set. " +
                "Ensure a D-Bus session bus is running.");

        _connection = new DBusConnection(sessionAddress);
        _connection.ConnectAsync().GetAwaiter().GetResult();
        return _connection;
    }

    // ── AT-SPI2 helpers ───────────────────────────────────────────────────────

    private (string busName, string path) FindApplicationByPid(DBusConnection conn, int pid)
    {
        // The registry root's accessible children are the registered applications.
        var apps = GetAccessibleChildren(conn, AtSpiRegistryService, AtSpiRegistryPath);

        foreach (var (busName, path) in apps)
        {
            var connPid = (int)GetConnectionPid(conn, busName);
            if (connPid == pid)
                return (busName, path);
        }

        throw new InvalidOperationException(
            $"No accessible application found for process {pid}. " +
            "Ensure the application supports AT-SPI2 accessibility and that " +
            "at-spi2-core is installed and running.");
    }

    private (string busName, string path) FindElementByPidAndId(
        DBusConnection conn, int pid, string elementId)
    {
        var match = TryFindElementByPidAndId(conn, pid, elementId);
        if (match is null)
        {
            throw new InvalidOperationException(
                $"Element '{elementId}' not found in the UI tree of process {pid}.");
        }

        return match.Value;
    }

    private (string busName, string path)? TryFindElementByPidAndId(
        DBusConnection conn, int pid, string elementId)
    {
        var (appBus, appPath) = FindApplicationByPid(conn, pid);

        var windows = GetAccessibleChildren(conn, appBus, appPath);
        if (windows.Length == 0)
            throw new InvalidOperationException(
                $"No accessible windows found for process {pid}.");

        var (winBus, winPath) = windows[0];

        return FindElementById(conn, winBus, winPath, elementId);
    }

    private (string, string)? FindElementById(
        DBusConnection conn, string busName, string path, string id)
    {
        var name = GetStringProperty(conn, busName, path, AtSpiAccessible, "Name");
        var role = GetRoleName(conn, busName, path);
        if (BuildId(role, name) == id)
            return (busName, path);

        foreach (var (childBus, childPath) in GetAccessibleChildren(conn, busName, path))
        {
            var found = FindElementById(conn, childBus, childPath, id);
            if (found.HasValue) return found;
        }

        return null;
    }

    private List<UiElement> WalkChildren(DBusConnection conn, string busName, string path)
    {
        var result = new List<UiElement>();
        foreach (var (childBus, childPath) in GetAccessibleChildren(conn, busName, path))
            result.Add(ToDto(conn, childBus, childPath));
        return result;
    }

    private UiElement ToDto(DBusConnection conn, string busName, string path)
    {
        var name = GetStringProperty(conn, busName, path, AtSpiAccessible, "Name");
        var role = GetRoleName(conn, busName, path);
        var (x, y, w, h) = TryGetExtents(conn, busName, path);

        Bounds? bounds = (w > 0 && h > 0) ? new Bounds(x, y, w, h) : null;

        var children = WalkChildren(conn, busName, path);
        return new UiElement(
            Id: BuildId(role, name),
            Type: role,
            Name: name,
            AutomationId: string.Empty,
            Bounds: bounds,
            Children: children.Count > 0 ? children : null);
    }

    private ElementStateResponse ToElementState(DBusConnection conn, string busName, string path)
    {
        var name = GetStringProperty(conn, busName, path, AtSpiAccessible, "Name");
        var role = GetRoleName(conn, busName, path);
        var (x, y, w, h) = TryGetExtents(conn, busName, path);

        Bounds? bounds = (w > 0 && h > 0) ? new Bounds(x, y, w, h) : null;

        return new ElementStateResponse(
            Id: BuildId(role, name),
            Type: role,
            Name: name,
            AutomationId: string.Empty,
            Bounds: bounds,
            Available: true);
    }

    /// <summary>Build a stable element ID: role + name (mirrors the Windows implementation).</summary>
    private static string BuildId(string role, string name)
    {
        var parts = new List<string> { role };
        if (!string.IsNullOrEmpty(name)) parts.Add(name.Replace(' ', '-'));
        return string.Join("-", parts).ToLowerInvariant();
    }

    // ── D-Bus calls ───────────────────────────────────────────────────────────

    private (string, string)[] GetAccessibleChildren(
        DBusConnection conn, string service, string path)
    {
        try
        {
            var msg = BuildNoArgCall(conn, service, path, AtSpiAccessible, "GetChildren");
            return conn.CallMethodAsync(msg, ReadAccessibleRefArray, null)
                       .GetAwaiter().GetResult();
        }
        catch
        {
            return [];
        }
    }

    private uint GetConnectionPid(DBusConnection conn, string busName)
    {
        var msg = BuildStringArgCall(conn,
            DBusBusService, DBusBusPath,
            DBusBusService, "GetConnectionUnixProcessID",
            "s", busName);
        return conn.CallMethodAsync(msg, ReadUInt32, null).GetAwaiter().GetResult();
    }

    private string GetStringProperty(
        DBusConnection conn, string service, string path,
        string @interface, string propertyName)
    {
        try
        {
            var msg = BuildTwoStringArgCall(conn,
                service, path,
                DBusProperties, "Get",
                "ss", @interface, propertyName);
            return conn.CallMethodAsync(msg, ReadVariantString, null)
                       .GetAwaiter().GetResult();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetRoleName(DBusConnection conn, string busName, string path)
    {
        try
        {
            var msg = BuildNoArgCall(conn, busName, path, AtSpiAccessible, "GetRoleName");
            return conn.CallMethodAsync(msg, ReadString, null).GetAwaiter().GetResult();
        }
        catch
        {
            return "unknown";
        }
    }

    private (int x, int y, int w, int h) GetExtents(
        DBusConnection conn, string busName, string path, uint coordType)
    {
        var msg = BuildUInt32ArgCall(conn, busName, path, AtSpiComponent, "GetExtents", coordType);
        return conn.CallMethodAsync(msg, ReadExtents, null).GetAwaiter().GetResult();
    }

    private (int x, int y, int w, int h) TryGetExtents(
        DBusConnection conn, string busName, string path)
    {
        try
        {
            return GetExtents(conn, busName, path, coordType: 0);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    private void DoAction(DBusConnection conn, string busName, string path, int index)
    {
        var msg = BuildInt32ArgCall(conn, busName, path, AtSpiAction, "DoAction", index);
        conn.CallMethodAsync(msg, ReadBool, null).GetAwaiter().GetResult();
    }

    private void GrabFocus(DBusConnection conn, string busName, string path)
    {
        try
        {
            var msg = BuildNoArgCall(conn, busName, path, AtSpiComponent, "GrabFocus");
            conn.CallMethodAsync(msg, ReadBool, null).GetAwaiter().GetResult();
        }
        catch { /* best-effort focus */ }
    }

    private void SetTextContents(DBusConnection conn, string busName, string path, string text)
    {
        var msg = BuildStringArgCall(conn, busName, path,
            AtSpiEditableText, "SetTextContents", "s", text);
        conn.CallMethodAsync(msg, ReadBool, null).GetAwaiter().GetResult();
    }

    // ── D-Bus message builders ────────────────────────────────────────────────

    private static MessageBuffer BuildNoArgCall(
        DBusConnection conn, string destination, string path,
        string @interface, string member)
    {
        var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, @interface, member,
            signature: null, MessageFlags.None);
        return writer.CreateMessage();
    }

    private static MessageBuffer BuildStringArgCall(
        DBusConnection conn, string destination, string path,
        string @interface, string member,
        string signature, string arg)
    {
        var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, @interface, member,
            signature, MessageFlags.None);
        writer.WriteString(arg);
        return writer.CreateMessage();
    }

    private static MessageBuffer BuildTwoStringArgCall(
        DBusConnection conn, string destination, string path,
        string @interface, string member,
        string signature, string arg1, string arg2)
    {
        var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, @interface, member,
            signature, MessageFlags.None);
        writer.WriteString(arg1);
        writer.WriteString(arg2);
        return writer.CreateMessage();
    }

    private static MessageBuffer BuildInt32ArgCall(
        DBusConnection conn, string destination, string path,
        string @interface, string member, int arg)
    {
        var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, @interface, member,
            "i", MessageFlags.None);
        writer.WriteInt32(arg);
        return writer.CreateMessage();
    }

    private static MessageBuffer BuildUInt32ArgCall(
        DBusConnection conn, string destination, string path,
        string @interface, string member, uint arg)
    {
        var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, @interface, member,
            "u", MessageFlags.None);
        writer.WriteUInt32(arg);
        return writer.CreateMessage();
    }

    // ── D-Bus reply readers ───────────────────────────────────────────────────

    private static (string busName, string path)[] ReadAccessibleRefArray(
        Message message, object? _)
    {
        var reader = message.GetBodyReader();
        var result = new List<(string, string)>();
        var ae = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(ae))
        {
            reader.AlignStruct();
            var busName = reader.ReadString();
            var path = reader.ReadObjectPathAsString();
            result.Add((busName, path));
        }
        return [.. result];
    }

    private static uint ReadUInt32(Message message, object? _) =>
        message.GetBodyReader().ReadUInt32();

    private static string ReadString(Message message, object? _) =>
        message.GetBodyReader().ReadString();

    private static bool ReadBool(Message message, object? _) =>
        message.GetBodyReader().ReadBool();

    private static string ReadVariantString(Message message, object? _)
    {
        var variant = message.GetBodyReader().ReadVariantValue();
        return variant.Type == VariantValueType.String ? variant.GetString() : string.Empty;
    }

    private static (int x, int y, int w, int h) ReadExtents(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        reader.AlignStruct();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        var w = reader.ReadInt32();
        var h = reader.ReadInt32();
        return (x, y, w, h);
    }

    // ── X11 P/Invoke ─────────────────────────────────────────────────────────

    private const int ZPixmap = 2;
    private const ulong AllPlanes = 0xFFFFFFFF;

    // XImage struct field offsets on 64-bit Linux (LP64 ABI):
    //  offset  0: int width
    //  offset  4: int height
    //  offset  8: int xoffset
    //  offset 12: int format
    //  offset 16: char* data  (8-byte pointer on 64-bit)
    //  offset 24: int byte_order
    //  offset 28: int bitmap_unit
    //  offset 32: int bitmap_bit_order
    //  offset 36: int bitmap_pad
    //  offset 40: int depth
    //  offset 44: int bytes_per_line
    //  offset 48: int bits_per_pixel
    private const int XImageWidthOffset       = 0;
    private const int XImageHeightOffset      = 4;
    private const int XImageDataOffset        = 16;
    private const int XImageBytesPerLineOffset = 44;

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable,
        int x, int y, uint width, uint height, ulong planeMask, int format);

    [DllImport("libX11.so.6")]
    private static extern void XDestroyImage(IntPtr ximage);

    /// <summary>Capture a rectangular region of the X11 screen and encode it as base64 PNG.</summary>
    private static string CaptureScreenRegion(int x, int y, int width, int height)
    {
        var display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
            throw new PlatformNotSupportedException(
                "Cannot connect to X11 display. " +
                "Ensure the DISPLAY environment variable is set.");
        try
        {
            var root = XDefaultRootWindow(display);
            var ximage = XGetImage(display, root, x, y,
                (uint)width, (uint)height, AllPlanes, ZPixmap);

            if (ximage == IntPtr.Zero)
                throw new InvalidOperationException(
                    "XGetImage returned null. " +
                    "The requested region may be outside the screen bounds.");
            try
            {
                int imgWidth     = Marshal.ReadInt32(ximage, XImageWidthOffset);
                int imgHeight    = Marshal.ReadInt32(ximage, XImageHeightOffset);
                IntPtr dataPtr   = Marshal.ReadIntPtr(ximage, XImageDataOffset);
                int bytesPerLine = Marshal.ReadInt32(ximage, XImageBytesPerLineOffset);

                var pixelData = new byte[imgHeight * bytesPerLine];
                Marshal.Copy(dataPtr, pixelData, 0, pixelData.Length);

                return EncodeAsPng(imgWidth, imgHeight, pixelData, bytesPerLine);
            }
            finally
            {
                XDestroyImage(ximage);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    // ── Minimal PNG encoder ───────────────────────────────────────────────────

    /// <summary>
    /// Encode raw BGRX pixel data (from X11 XGetImage ZPixmap) as a PNG and
    /// return it as a base64 string.
    /// </summary>
    private static string EncodeAsPng(int width, int height, byte[] data, int bytesPerLine)
    {
        // Build raw scanline data with PNG "None" (0x00) filter per row.
        // PNG uses RGB (3 bytes/pixel); XImage ZPixmap supplies BGRA/BGRX (4 bytes/pixel).
        int rowSize = 1 + width * 3; // 1 filter byte + 3 RGB bytes per pixel
        var raw = new byte[height * rowSize];

        for (int row = 0; row < height; row++)
        {
            raw[row * rowSize] = 0; // filter = None
            for (int col = 0; col < width; col++)
            {
                int src = row * bytesPerLine + col * 4;
                int dst = row * rowSize + 1 + col * 3;
                raw[dst]     = data[src + 2]; // R (B-G-R-X order)
                raw[dst + 1] = data[src + 1]; // G
                raw[dst + 2] = data[src];     // B
            }
        }

        // Compress scanlines with ZLib (deflate + zlib header).
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);
        var idat = compressed.ToArray();

        // Assemble PNG bytes.
        using var png = new MemoryStream();

        // Signature
        png.Write((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR (13 bytes): width, height, bit-depth=8, color-type=2 (RGB), rest 0
        var ihdr = new byte[13];
        WriteBigEndianInt32(ihdr, 0, width);
        WriteBigEndianInt32(ihdr, 4, height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // color type: RGB
        WriteChunk(png, "IHDR"u8, ihdr);

        // IDAT
        WriteChunk(png, "IDAT"u8, idat);

        // IEND
        WriteChunk(png, "IEND"u8, []);

        return Convert.ToBase64String(png.ToArray());
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
    {
        WriteBigEndianUInt32(stream, (uint)data.Length);
        stream.Write(type);
        stream.Write(data);
        WriteBigEndianUInt32(stream, ComputeCrc32(type, data));
    }

    private static void WriteBigEndianInt32(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteBigEndianUInt32(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }

    // Standard CRC-32 (ISO 3309 / ITU-T V.42) used by the PNG specification.
    private const uint Crc32InitialValue = 0xFFFF_FFFFu;
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
                c = (c & 1) == 0 ? (c >> 1) : (0xEDB88320u ^ (c >> 1));
            table[i] = c;
        }
        return table;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> typeBytes, byte[] data)
    {
        uint crc = Crc32InitialValue;
        foreach (byte b in typeBytes)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
#endif
