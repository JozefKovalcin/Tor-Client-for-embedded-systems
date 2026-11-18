using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Tor.Core;

namespace Tor.Adapters.Windows;

public static class WindowsOnlineTorSettings
{
    public static string Configuration(int socksPort)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(socksPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(socksPort, 65535);
        return "SocksPort 127.0.0.1:" + socksPort.ToString(CultureInfo.InvariantCulture) + " IsolateSOCKSAuth\n" +
            "SocksPolicy accept 127.0.0.1/32\nSocksPolicy reject *\nSafeSocks 1\n" +
            "ExitRelay 0\nExitPolicy reject *:*\nDisableNetwork 1\n";
    }

    // Diagnostic only. Tor still has to bind the port after authenticating and
    // the actual listener must belong to the launched process before it is used.
    internal static WindowsTorFailure? CheckPort(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.ExclusiveAddressUse = true;
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return null;
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return WindowsTorFailure.SocksPortInUse;
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.AccessDenied)
        {
            // Winsock also reports AccessDenied when another exclusive listener owns the port.
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(endpoint => endpoint.Port == port &&
                (endpoint.Address.Equals(IPAddress.Loopback) || endpoint.Address.Equals(IPAddress.Any) ||
                 endpoint.Address.Equals(IPAddress.IPv6Any))) ? WindowsTorFailure.SocksPortInUse : WindowsTorFailure.SocksPortUnavailable;
        }
    }
}
