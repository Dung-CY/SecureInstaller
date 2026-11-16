namespace Installer.Core.Model;

public enum InstallerType { Msi, Exe, Zip, Script }
public enum UserRole { User, Admin }

public record DetectRule(string? ProductCode, string? DisplayNameRegex);
public record PostAction(string Type, string? Src, string Dst);
public record RuleSpec(string Type);
public record KeywordScanConfig(List<string> Keywords, List<string> Paths, int MaxSeconds);
public record CrackSignature(string Kind, string Pattern);

public record PackageEntry(string Id, string Name, string Version, InstallerType InstallerType, string InstallerFile,
    string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> Docs,
    List<PostAction> PostActions, List<RuleSpec> Rules, string BlobFile, string Sha256, string KeyB64,
    long BlobSize = 0, string? DocsKeyB64 = null, string? IconPngB64 = null, UninstallSpec? Uninstall = null,
    string? PreScript = null, string? PostScript = null, int ScriptTimeoutSec = 600,
    InstallerKind Kind = InstallerKind.Unknown, string? UninstallScript = null, List<string>? Extras = null,
    DataCopy? Data = null, bool Hidden = false);

/// <summary>After install, copy the listed files of DuLieu\&lt;id&gt; (next to the exe, not encrypted) into TargetDir.
/// Each line: "tên trong DuLieu =&gt; tên khi chép" or just a name; {DonVi} is the session's unit.</summary>
public record DataCopy(string TargetDir, List<string>? Files);

/// <summary>Type: "msi" | "command" | "zip".</summary>
public record UninstallSpec(string Type, string? Command = null, string? Args = null);
public record BundleItem(string PackageId, string? Version = null);
public record Bundle(string Id, string Name, string Description, List<BundleItem> Items);

public record Catalog(int Version, DateTimeOffset UpdatedAt, List<PackageEntry> Packages,
    KeywordScanConfig KeywordScan, List<CrackSignature> CrackSignatures,
    List<Bundle>? Bundles = null, int Format = 1)
{
    public static Catalog Empty() => new(0, DateTimeOffset.UtcNow, new(),
        new KeywordScanConfig(new(), new(), 60),
        Installer.Core.Licensing.CrackSignatureDefaults.All.ToList(), null, 2);
}
