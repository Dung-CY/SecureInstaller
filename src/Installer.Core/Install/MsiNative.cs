using System.Runtime.InteropServices;

namespace Installer.Core.Install;

internal static class MsiNative
{
    public const int INSTALLUILEVEL_NONE = 2;
    // msi.h: INSTALLLOGMODE_x = 1 << (INSTALLMESSAGE_x >> 24)
    public const uint INSTALLLOGMODE_ACTIONSTART = 0x00000100;
    public const uint INSTALLLOGMODE_PROGRESS = 0x00000400;
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

    public const int INSTALLSTATE_ABSENT = 2;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiConfigureProductExW(string szProduct, int iInstallLevel, int eInstallState, string szCommandLine);

    public const uint MSIOPENPACKAGEFLAGS_IGNOREMACHINESTATE = 1;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiOpenPackageExW(string szPackagePath, uint dwOptions, out IntPtr hProduct);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiGetProductPropertyW(IntPtr hProduct, string szProperty, char[]? lpValueBuf, ref uint pcchValueBuf);

    [DllImport("msi.dll")]
    public static extern uint MsiCloseHandle(IntPtr hAny);
}
