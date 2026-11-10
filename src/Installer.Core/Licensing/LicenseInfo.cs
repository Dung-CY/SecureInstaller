namespace Installer.Core.Licensing;

public enum LicenseVerdict { Genuine, Suspicious, CrackSuspected, Unknown, NotInstalled }

public enum LicenseChannel { Retail, Oem, VolumeMak, VolumeKms, Unknown }

public record LicenseInfo(string Product, string? Edition, LicenseChannel Channel, bool? Activated,
    string? PartialKey, string? KmsHost, LicenseVerdict Verdict, List<string> Evidence);

public record LicenseRaw(string Product, string? Edition, string? ChannelText, string? Description,
    int? LicenseStatus, string? PartialKey, string? KmsHost);
