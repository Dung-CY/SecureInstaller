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
        public UninstallSpec? Uninstall { get; set; }
        public string? PreScript { get; set; }
        public string? PostScript { get; set; }
        public int? ScriptTimeoutSec { get; set; }
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

        if (m.InstallerType == InstallerType.Msi && !string.IsNullOrWhiteSpace(m.SilentArgs) &&
            m.SilentArgs.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(t => t[0] is '/' or '-'))
            throw new InvalidDataException("silent_args của gói MSI chỉ được chứa thuộc tính dạng TÊN=giá_trị (không dùng /qn, /norestart...)");

        if (m.Uninstall is { } u)
        {
            if (u.Type is not ("msi" or "command" or "zip"))
                throw new InvalidDataException("uninstall.type phải là msi, command hoặc zip");
            if (u.Type == "command" && string.IsNullOrWhiteSpace(u.Command))
                throw new InvalidDataException("uninstall.command là bắt buộc khi type là command");
        }

        var isScript = m.InstallerType == InstallerType.Script;
        var instSub = isScript ? "scripts" : "installer";
        var installerPath = Path.GetFullPath(Path.Combine(sourceFolder, instSub, m.InstallerFile));
        var installerDir = Path.GetFullPath(Path.Combine(sourceFolder, instSub)) + Path.DirectorySeparatorChar;
        if (!installerPath.StartsWith(installerDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(installerPath))
            throw new InvalidDataException($"Không tìm thấy installer_file: {instSub}\\{m.InstallerFile}");
        if (isScript) CheckScript("installer_file", m.InstallerFile);
        foreach (var (field, name) in new[] { ("pre_script", m.PreScript), ("post_script", m.PostScript) })
        {
            if (string.IsNullOrEmpty(name)) continue;
            CheckScript(field, name);
            if (name != Path.GetFileName(name) || !File.Exists(Path.Combine(sourceFolder, "scripts", name)))
                throw new InvalidDataException($"Không tìm thấy {field}: scripts\\{name}");
        }

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
            docs, m.PostActions ?? new(), m.Rules ?? new(), "", "", "", 0, null, null, m.Uninstall,
            string.IsNullOrEmpty(m.PreScript) ? null : m.PreScript, string.IsNullOrEmpty(m.PostScript) ? null : m.PostScript,
            m.ScriptTimeoutSec ?? 600);
        return new Manifest(entry, docs);
    }

    static void CheckScript(string field, string name)
    {
        if (!Installer.Core.Install.ScriptRunner.Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{field} chỉ được là .ps1, .cmd hoặc .bat");
    }

    const string BlobSuffix = ".pkg.enc";

    public static string DocsDirFor(PackageEntry e, string vaultRoot)
    {
        var stem = e.BlobFile.EndsWith(BlobSuffix, StringComparison.Ordinal)
            ? e.BlobFile[..^BlobSuffix.Length] : e.BlobFile;
        return Path.Combine(vaultRoot, "vault", "docs", stem);
    }

    const int MaxIconBytes = 24 * 1024;

    static string? ResolveIcon(string sourceFolder, PackageEntry entry, Func<string, byte[]?>? iconFromExe)
    {
        var iconPath = Path.Combine(sourceFolder, "icon.png");
        if (File.Exists(iconPath))
        {
            var bytes = File.ReadAllBytes(iconPath);
            if (bytes.Length > MaxIconBytes || bytes.Length < 8 ||
                !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                throw new InvalidDataException("icon.png phải là ảnh PNG ≤ 24 KB");
            return Convert.ToBase64String(bytes);
        }
        if (entry.InstallerType == InstallerType.Exe && iconFromExe is not null)
        {
            byte[]? png = null;
            try { png = iconFromExe(Path.Combine(sourceFolder, "installer", entry.InstallerFile)); } catch { }
            if (png is { Length: > 0 } && png.Length <= MaxIconBytes) return Convert.ToBase64String(png);
        }
        return null;
    }

    public static PackageEntry Build(string sourceFolder, string vaultRoot, Func<string, byte[]?>? iconFromExe = null)
    {
        var entry = ReadManifest(sourceFolder).Entry;
        var iconB64 = ResolveIcon(sourceFolder, entry, iconFromExe);
        var vaultDir = Path.Combine(vaultRoot, "vault");
        var pkgDir = Path.Combine(vaultDir, "packages");
        var stem = $"{entry.Id}-{entry.Version}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
        var docsOut = Path.Combine(vaultDir, "docs", stem);
        Directory.CreateDirectory(pkgDir);
        var blobName = stem + ".pkg.enc";
        var blobPath = Path.Combine(pkgDir, blobName);
        var tmpZip = Path.Combine(Path.GetTempPath(), "SI_zip_" + Guid.NewGuid().ToString("N") + ".zip");
        var key = KeyDerivation.NewKey();
        var docsKey = KeyDerivation.NewKey();
        var ok = false;
        var created = false;
        try
        {
            using (var zipFs = new FileStream(tmpZip, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(zipFs, ZipArchiveMode.Create))
            {
                foreach (var top in new[] { "installer", "extras", "scripts" })
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
            using (var output = new FileStream(blobPath, FileMode.CreateNew, FileAccess.Write))
            {
                created = true;
                PackageCrypto.Encrypt(plain, output, key);
            }

            if (entry.Docs.Count > 0)
            {
                Directory.CreateDirectory(docsOut);
                foreach (var d in entry.Docs)
                {
                    using var plain = File.OpenRead(Path.Combine(sourceFolder, "docs", d));
                    using var output = new FileStream(Path.Combine(docsOut, d + ".enc"), FileMode.CreateNew, FileAccess.Write);
                    PackageCrypto.Encrypt(plain, output, docsKey);
                }
            }

            string sha;
            long blobSize;
            using (var fs = File.OpenRead(blobPath))
            {
                blobSize = fs.Length;
                sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
            ok = true;
            return entry with { BlobFile = blobName, Sha256 = sha, KeyB64 = Convert.ToBase64String(key),
                BlobSize = blobSize, DocsKeyB64 = Convert.ToBase64String(docsKey), IconPngB64 = iconB64 };
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { }
            if (!ok && created)
            {
                try { File.Delete(blobPath); } catch { }
                try { if (Directory.Exists(docsOut)) Directory.Delete(docsOut, true); } catch { }
            }
        }
    }
}
