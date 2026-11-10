using Installer.Core.Model;
using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Licensing;

public sealed class LicenseScanner
{
    private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
    private const string OfficeAppId = "0ff1ce15-a989-479d-af46-f275c6370663";
    private const string C2RPath = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";
    private const string ReadFailed = "Không đọc được thông tin bản quyền";

    private readonly IWmi _wmi;
    private readonly IRegistryReader _reg;
    private readonly ISystemInfo _sys;

    public LicenseScanner(IWmi wmi, IRegistryReader reg, ISystemInfo sys)
    {
        _wmi = wmi; _reg = reg; _sys = sys;
    }

    public LicenseInfo ScanWindows(IReadOnlyList<CrackSignature> sigs)
    {
        var (raw, failed) = QueryWmi(WindowsAppId);
        if (raw == null && failed)
            return new LicenseInfo("Windows", null, LicenseChannel.Unknown, null, null, null,
                LicenseVerdict.Unknown, new() { ReadFailed });
        return CrackDetector.Evaluate(raw, _sys, sigs, isOffice: false);
    }

    public LicenseInfo ScanOffice(IReadOnlyList<CrackSignature> sigs)
    {
        var (raw, failed) = QueryWmi(OfficeAppId);
        raw ??= ReadC2R();
        if (raw == null && failed)
            return new LicenseInfo("Microsoft Office", null, LicenseChannel.Unknown, null, null, null,
                LicenseVerdict.Unknown, new() { ReadFailed });
        return CrackDetector.Evaluate(raw, _sys, sigs, isOffice: true);
    }

    private (LicenseRaw? raw, bool failed) QueryWmi(string appId)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = _wmi.Query("SELECT Name, Description, LicenseStatus, PartialProductKey, ProductKeyChannel, " +
                "KeyManagementServiceMachine, DiscoveredKeyManagementServiceMachineName FROM SoftwareLicensingProduct " +
                $"WHERE ApplicationID='{appId}' AND PartialProductKey IS NOT NULL");
        }
        catch (Exception) { return (null, true); }

        if (rows.Count == 0) return (null, false);
        var row = rows.FirstOrDefault(r => Status(r) == 1) ?? rows[0];
        var kms = Str(row, "KeyManagementServiceMachine");
        if (string.IsNullOrEmpty(kms)) kms = Str(row, "DiscoveredKeyManagementServiceMachineName");
        if (string.IsNullOrEmpty(kms)) kms = null;
        return (new LicenseRaw(Str(row, "Name") ?? "", Str(row, "Description"), Str(row, "ProductKeyChannel"),
            Str(row, "Description"), Status(row), Str(row, "PartialProductKey"), kms), false);
    }

    private LicenseRaw? ReadC2R()
    {
        var ids = _reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64, C2RPath, "ProductReleaseIds") as string;
        if (string.IsNullOrEmpty(ids)) return null;
        var ver = _reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64, C2RPath, "VersionToReport") as string;
        return new LicenseRaw(ids, ver, null, ids, null, null, null);
    }

    private static string? Str(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) ? v as string : null;

    private static int? Status(IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue("LicenseStatus", out var v)) return null;
        return v switch
        {
            int i => i,
            uint u when u <= int.MaxValue => (int)u,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            _ => null,
        };
    }
}
