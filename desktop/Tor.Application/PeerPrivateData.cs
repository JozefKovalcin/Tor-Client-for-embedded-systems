using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Tor.Application;

/// <summary>Windows DPAPI for private per-user profile payloads. No machine-scope option is exposed.</summary>
internal static class PeerPrivateData
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    internal static byte[] Protect(byte[] clear) => Transform(clear, decrypt: false);
    internal static byte[] Unprotect(byte[] payload) => Transform(payload, decrypt: true);

    private static byte[] Transform(byte[] source, bool decrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows user scope is required.");
        var pinned = GCHandle.Alloc(source, GCHandleType.Pinned);
        var input = new DataBlob { Data = pinned.AddrOfPinnedObject(), Length = source.Length };
        DataBlob output = default;
        try
        {
            const uint noUi = 1;
            bool ok = decrypt
                ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, noUi, out output)
                : CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, noUi, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastPInvokeError());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                if (decrypt)
                    for (int offset = 0; offset < output.Length; offset++) Marshal.WriteByte(output.Data, offset, 0);
                _ = LocalFree(output.Data);
            }
            pinned.Free();
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr allocation);
}
