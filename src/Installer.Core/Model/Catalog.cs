namespace Installer.Core.Model;

public enum InstallerType { Msi, Exe, Zip }
public enum UserRole { User, Admin }

public record DetectRule(string? ProductCode, string? DisplayNameRegex);
public record PostAction(string Type, string? Src, string Dst);
public record RuleSpec(string Type);
public record KeywordScanConfig(List<string> Keywords, List<string> Paths, int MaxSeconds);
public record CrackSignature(string Kind, string Pattern);

public record PackageEntry(string Id, string Name, string Version, InstallerType InstallerType, string InstallerFile,
    string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> Docs,
    List<PostAction> PostActions, List<RuleSpec> Rules, string BlobFile, string Sha256, string KeyB64);

public record Catalog(int Version, DateTimeOffset UpdatedAt, List<PackageEntry> Packages,
    KeywordScanConfig KeywordScan, List<CrackSignature> CrackSignatures)
{
    public static Catalog Empty() => new(0, DateTimeOffset.UtcNow, new(),
        new KeywordScanConfig(new(), new(), 60), new());
}
