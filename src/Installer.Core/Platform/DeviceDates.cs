using System.Runtime.InteropServices;

namespace Installer.Core.Platform;

/// <summary>When a device was first installed and last plugged in, as Windows records it for the device instance.</summary>
public interface IDeviceDates
{
    (DateTime? First, DateTime? Last) Get(string deviceInstanceId);
}

/// <summary>Reads DEVPKEY_Device_FirstInstallDate / LastArrivalDate through cfgmgr32, also for unplugged devices.</summary>
public sealed class DeviceDates : IDeviceDates
{
    [StructLayout(LayoutKind.Sequential)]
    struct DevPropKey { public Guid Fmtid; public uint Pid; }

    static readonly Guid DeviceDatesFmtid = new("83da6326-97a6-4088-9453-a1923f573b29");
    const uint FirstInstallPid = 101, LastArrivalPid = 102;
    const uint CM_LOCATE_DEVNODE_PHANTOM = 1, DEVPROP_TYPE_FILETIME = 0x10;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern uint CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint type, byte[] buffer, ref uint size, uint flags);

    public (DateTime? First, DateTime? Last) Get(string deviceInstanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var inst, deviceInstanceId, CM_LOCATE_DEVNODE_PHANTOM) != 0) return (null, null);
            return (Read(inst, FirstInstallPid), Read(inst, LastArrivalPid));
        }
        catch (Exception) { return (null, null); }   // missing API or odd id: dates are optional
    }

    static DateTime? Read(uint inst, uint pid)
    {
        var key = new DevPropKey { Fmtid = DeviceDatesFmtid, Pid = pid };
        var buf = new byte[8];
        uint size = 8;
        if (CM_Get_DevNode_PropertyW(inst, ref key, out var type, buf, ref size, 0) != 0 || type != DEVPROP_TYPE_FILETIME || size != 8) return null;
        var ft = BitConverter.ToInt64(buf, 0);
        return ft <= 0 ? null : DateTime.FromFileTimeUtc(ft).ToLocalTime();
    }
}
