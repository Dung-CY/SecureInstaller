using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Installer.Core.Crypto;
using Installer.Core.Model;

namespace Installer.Core.Vault;

public static class PackageBuilder
{
    static readonly Regex SafeName = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    sealed class ManifestDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public InstallerType? InstallerType { get; set; }
        public string? InstallerFile { get; set; }
        public string? SilentArgs { get; set; }
        public string? TargetDir { get; set; }
        public int? TimeoutSec { get; set; }
        public DetectRule? Detect { get; set; }
        public List<string>? Docs { get; set; }
        public List<PostAction>? PostActions { get; set; }
        public List<RuleSpec>? Rules { get; set; }
    }

    internal sealed record Manifest(PackageEntry Entry, List<string> DocNames);

    internal static Manifest ReadManifest(string sourceFolder)
    {
        var path = Path.Combine(sourceFolder, "manifest.json");
        if (!File.Exists(path)) throw new InvalidDataException("Không tìm thấy manifest.json");
        ManifestDto? m;
        try { m = JsonSerializer.Deserialize<ManifestDto>(File.ReadAllBytes(path), Json.Options); }
        catch (JsonException e) { throw new InvalidDataException("manifest.json không hợp lệ: " + e.Message); }
        if (m is null) throw new InvalidDataException("manifest.json trống");

        if (string.IsNullOrWhiteSpace(m.Id)) throw new InvalidDataException("Thiếu trường: id");
        if (string.IsNullOrWhiteSpace(m.Name)) throw new InvalidDataException("Thiếu trường: name");
        if (string.IsNullOrWhiteSpace(m.Version)) throw new InvalidDataException("Thiếu trường: version");
        if (m.InstallerType is null) throw new InvalidDataException("Thiếu trường: installer_type");
        if (string.IsNullOrWhiteSpace(m.InstallerFile)) throw new InvalidDataException("Thiếu trường: installer_file");
        if (!SafeName.IsMatch(m.Id)) throw new InvalidDataException("Trường id chứa ký tự không hợp lệ");
        if (!SafeName.IsMatch(m.Version)) throw new InvalidDataException("Trường version chứa ký tự không hợp lệ");

        var installerPath = Path.GetFullPath(Path.Combine(sourceFolder, "installer", m.InstallerFile));
        var installerDir = Path.GetFullPath(Path.Combine(sourceFolder, "installer")) + Path.DirectorySeparatorChar;
        if (!installerPath.StartsWith(installerDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(installerPath))
            throw new InvalidDataException($"Không tìm thấy installer_file: installer\\{m.InstallerFile}");

        var docsDir = Path.Combine(sourceFolder, "docs");
        List<string> docs;
        if (m.Docs is { } listed)
        {
            foreach (var d in listed)
                if (string.IsNullOrEmpty(d) || d != Path.GetFileName(d) || !File.Exists(Path.Combine(docsDir, d)))
                    throw new InvalidDataException($"Không tìm thấy tài liệu trong docs: {d}");
            docs = listed.ToList();
        }
        else
        {
            docs = Directory.Exists(docsDir)
                ? Directory.GetFiles(docsDir).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToList()
                : new List<string>();
        }

        var entry = new PackageEntry(m.Id, m.Name, m.Version, m.InstallerType.Value, m.InstallerFile,
            m.SilentArgs ?? "", m.TargetDir, m.TimeoutSec ?? 1800, m.Detect ?? new DetectRule(null, null),
            docs, m.PostActions ?? new(), m.Rules ?? new(), "", "", "");
        return new Manifest(entry, docs);
    }

    public static PackageEntry Build(string sourceFolder, string vaultRoot)
    {
        var entry = ReadManifest(sourceFolder).Entry;
        var vaultDir = Path.Combine(vaultRoot, "vault");
        var pkgDir = Path.Combine(vaultDir, "packages");
        var docsOut = Path.Combine(vaultDir, "docs", $"{entry.Id}-{entry.Version}");
        Directory.CreateDirectory(pkgDir);
        var blobName = $"{entry.Id}-{entry.Version}.pkg.enc";
        var blobPath = Path.Combine(pkgDir, blobName);
        var tmpZip = Path.Combine(Path.GetTempPath(), "SI_zip_" + Guid.NewGuid().ToString("N") + ".zip");
        var key = KeyDerivation.NewKey();
        var ok = false;
        try
        {
            using (var zipFs = new FileStream(tmpZip, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(zipFs, ZipArchiveMode.Create))
            {
                foreach (var top in new[] { "installer", "extras" })
                {
                    var dir = Path.Combine(sourceFolder, top);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                        zip.CreateEntryFromFile(f, top + "/" + rel, CompressionLevel.Optimal);
                    }
                }
            }
            using (var plain = File.OpenRead(tmpZip))
            using (var output = new FileStream(blobPath, FileMode.Create, FileAccess.Write))
                PackageCrypto.Encrypt(plain, output, key);

            if (entry.Docs.Count > 0)
            {
                Directory.CreateDirectory(docsOut);
                foreach (var d in entry.Docs)
                {
                    using var plain = File.OpenRead(Path.Combine(sourceFolder, "docs", d));
                    using var output = new FileStream(Path.Combine(docsOut, d + ".enc"), FileMode.Create, FileAccess.Write);
                    PackageCrypto.Encrypt(plain, output, key);
                }
            }

            string sha;
            using (var fs = File.OpenRead(blobPath))
                sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            ok = true;
            return entry with { BlobFile = blobName, Sha256 = sha, KeyB64 = Convert.ToBase64String(key) };
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { }
            if (!ok)
            {
                try { File.Delete(blobPath); } catch { }
                try { if (Directory.Exists(docsOut)) Directory.Delete(docsOut, true); } catch { }
            }
        }
    }
}
