using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tor.Adapters.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsTcpOwner
{
    private const int TcpEstablished = 5;
    private const int TcpListening = 2;
    private const int OwnerPidListener = 3;
    private const int OwnerPidConnections = 4;
    private const int Localhost = 0x0100007f;
    private const uint InsufficientBuffer = 122;
    private const int RowBytes = 6 * sizeof(int);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(nint table, ref uint bytes, int order, int family,
        int tableClass, uint reserved);

    internal static bool OwnsListener(int pid, int port) => Find(pid, port, null);

    internal static bool OwnsPeer(int pid, int port, IPEndPoint client) =>
        client.Address.Equals(IPAddress.Loopback) && Find(pid, port, client.Port);

    private static bool Find(int pid, int port, int? clientPort)
    {
        const int inet = 2;
        var tableClass = clientPort is null ? OwnerPidListener : OwnerPidConnections;
        uint bytes = 0;
        if (GetExtendedTcpTable(0, ref bytes, 0, inet, tableClass, 0) != InsufficientBuffer) return false;
        for (int attempt = 0; attempt < 3 && bytes is >= sizeof(int) and <= 16 * 1024 * 1024; attempt++)
        {
            var capacity = (int)bytes;
            var table = Marshal.AllocHGlobal(capacity);
            try
            {
                var result = GetExtendedTcpTable(table, ref bytes, 0, inet, tableClass, 0);
                if (result == InsufficientBuffer) continue;
                if (result != 0) return false;
                var count = Marshal.ReadInt32(table);
                if (count < 0 || count > (capacity - sizeof(int)) / RowBytes) return false;
                var localPort = IPAddress.HostToNetworkOrder(unchecked((short)port)) & 0xffff;
                var remotePort = IPAddress.HostToNetworkOrder(unchecked((short)clientPort.GetValueOrDefault())) & 0xffff;
                var expectedState = clientPort is null ? TcpListening : TcpEstablished;
                for (var index = 0; index < count; index++)
                {
                    var row = table + sizeof(int) + index * RowBytes;
                    if (Marshal.ReadInt32(row) != expectedState || Marshal.ReadInt32(row, 4) != Localhost ||
                        (Marshal.ReadInt32(row, 8) & 0xffff) != localPort || Marshal.ReadInt32(row, 20) != pid)
                        continue;
                    if (clientPort is null || Marshal.ReadInt32(row, 12) == Localhost &&
                        (Marshal.ReadInt32(row, 16) & 0xffff) == remotePort) return true;
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        return false;
    }
}
