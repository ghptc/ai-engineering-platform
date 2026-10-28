using System.Runtime.InteropServices;
using System.Net.Sockets;
namespace AIEngineeringPlatform.LocalExecutor;

internal static class UnixSocketFiles
{
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx { [FieldOffset(28)] public ushort Mode; }
    [DllImport("libc", SetLastError = true, EntryPoint = "statx")]
    private static extern int ReadStat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Statx result);
    public static async Task RemoveStaleSocketAsync(string path)
    {
        if (!Path.GetFullPath(path).EndsWith(".sock", StringComparison.Ordinal)) throw new ArgumentException("Executor socket must end in .sock.");
        if (ReadStat(-100, path, 0x100, 1, out var stat) != 0)
        {
            if (Marshal.GetLastPInvokeError() == 2) return;
            throw new IOException("Cannot inspect Unix socket file type.");
        }
        if ((stat.Mode & 0xF000) != 0xC000) throw new IOException("Refusing to remove a non-socket filesystem entry.");
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await probe.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token); throw new InvalidOperationException("An active executor already owns this socket path."); }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused) { File.Delete(path); }
    }
}
