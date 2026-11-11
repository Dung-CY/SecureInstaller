using Installer.Core.Licensing;

namespace Installer.Core.Scanning;

public record ScanReport(DateTimeOffset ScannedAt, HardwareInfo Hardware, NetworkHistory Network,
    LicenseInfo Windows, LicenseInfo Office, KeywordScanResult Keywords);
