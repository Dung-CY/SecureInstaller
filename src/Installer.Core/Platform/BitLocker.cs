using System.Management;

namespace Installer.Core.Platform;

/// <summary>Result of turning BitLocker off: which drives began decrypting, which were already off, whether it is supported here.</summary>
public sealed record BitLockerResult(bool Supported, IReadOnlyList<string> TurnedOff, IReadOnlyList<string> AlreadyOff);

public interface IBitLocker
{
    /// <summary>Begins decryption on every protected fixed drive. Never throws.</summary>
    BitLockerResult DisableAll();
}

/// <summary>
/// Turns BitLocker off via WMI (root\cimv2\security\MicrosoftVolumeEncryption). Absent on Windows Home, where the
/// namespace fails to connect and Supported is false. Decryption runs in the background after Decrypt() returns.
/// </summary>
public sealed class BitLockerControl : IBitLocker
{
    public BitLockerResult DisableAll()
    {
        var turnedOff = new List<string>();
        var alreadyOff = new List<string>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\cimv2\security\MicrosoftVolumeEncryption");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_EncryptableVolume"));
            using var volumes = searcher.Get();
            foreach (ManagementObject vol in volumes)
                using (vol)
                {
                    var drive = vol["DriveLetter"] as string ?? "";
                    uint status;
                    try
                    {
                        var r = vol.InvokeMethod("GetConversionStatus", null, null);
                        // ConversionStatus: 0 = fully decrypted, anything else = encrypted or in progress.
                        status = r?["ConversionStatus"] is { } cs ? Convert.ToUInt32(cs) : 0;
                    }
                    catch (Exception) { continue; }
                    if (status == 0) { if (drive.Length > 0) alreadyOff.Add(drive); continue; }
                    try
                    {
                        vol.InvokeMethod("DisableKeyProtectors", null, null);
                        vol.InvokeMethod("Decrypt", null, null);
                        if (drive.Length > 0) turnedOff.Add(drive);
                    }
                    catch (Exception) { }   // one locked/other-user volume must not stop the rest
                }
            return new BitLockerResult(true, turnedOff, alreadyOff);
        }
        catch (Exception)
        {
            return new BitLockerResult(false, turnedOff, alreadyOff);   // namespace missing (Home) or access denied
        }
    }
}
