using System.Runtime.InteropServices;

namespace Installer.Core.Install;

internal static class MsiNative
{
    public const int INSTALLUILEVEL_NONE = 2;
    public const uint INSTALLLOGMODE_ACTIONSTART = 0x00000400;
    public const uint INSTALLLOGMODE_PROGRESS = 0x00010000;
    public const uint MSG_ACTIONSTART = 0x08000000;
    public const uint MSG_PROGRESS = 0x0A000000;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public delegate int ExternalUIHandler(IntPtr context, uint messageType, [MarshalAs(UnmanagedType.LPWStr)] string message);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern int MsiSetInternalUI(int dwUILevel, IntPtr phWnd);

    [DllImport("msi.dll", EntryPoint = "MsiSetExternalUIW", CharSet = CharSet.Unicode)]
    public static extern IntPtr MsiSetExternalUI(ExternalUIHandler? puiHandler, uint dwMessageFilter, IntPtr pvContext);

    [DllImport("msi.dll", EntryPoint = "MsiSetExternalUIW", CharSet = CharSet.Unicode)]
    public static extern IntPtr MsiRestoreExternalUI(IntPtr puiHandler, uint dwMessageFilter, IntPtr pvContext);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiInstallProductW(string szPackagePath, string szCommandLine);
}
