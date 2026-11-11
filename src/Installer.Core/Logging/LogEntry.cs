namespace Installer.Core.Logging;

public record LogEntry(DateTimeOffset Ts, string Username, string Hostname, string? BiosSerial, string Action,
    string? PackageId, string? PackageVersion, string Status, int? ExitCode, string? Detail);
