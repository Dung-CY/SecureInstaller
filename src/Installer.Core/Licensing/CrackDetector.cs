using System.Text.RegularExpressions;
using Installer.Core.Model;
using Installer.Core.Platform;

namespace Installer.Core.Licensing;

public static class CrackDetector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static LicenseInfo Evaluate(LicenseRaw? raw, ISystemInfo sys, IReadOnlyList<CrackSignature> sigs, bool isOffice)
    {
        if (raw == null)
        {
            return isOffice
                ? new LicenseInfo("Microsoft Office", null, LicenseChannel.Unknown, null, null, null,
                    LicenseVerdict.NotInstalled, new())
                : new LicenseInfo("Windows", null, LicenseChannel.Unknown, null, null, null,
                    LicenseVerdict.Unknown, new() { "Không đọc được thông tin bản quyền" });
        }

        var channel = MapChannel(raw.ChannelText);
        if (channel == LicenseChannel.Unknown) channel = MapChannel(raw.Description);
        bool? activated = raw.LicenseStatus is null ? null : raw.LicenseStatus == 1;

        var evidence = EvaluateSignatures(raw, sys, sigs);
        LicenseVerdict verdict;
        if (evidence.Count > 0) verdict = LicenseVerdict.CrackSuspected;
        else if (activated == false)
        {
            verdict = LicenseVerdict.Suspicious;
            evidence.Add("Chưa kích hoạt");
        }
        else if (channel == LicenseChannel.VolumeKms && !sys.IsDomainJoined)
        {
            verdict = LicenseVerdict.Suspicious;
            evidence.Add("Key KMS trên máy không thuộc domain");
        }
        else if (activated == true) verdict = LicenseVerdict.Genuine;
        else verdict = LicenseVerdict.Unknown;

        return new LicenseInfo(raw.Product, raw.Edition, channel, activated, raw.PartialKey, raw.KmsHost, verdict, evidence);
    }

    public static LicenseChannel MapChannel(string? text)
    {
        if (string.IsNullOrEmpty(text)) return LicenseChannel.Unknown;
        bool Has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("OEM")) return LicenseChannel.Oem;
        if (Has("MAK")) return LicenseChannel.VolumeMak;
        if (Has("KMS") || Has("GVLK")) return LicenseChannel.VolumeKms;
        if (Has("Retail")) return LicenseChannel.Retail;
        return LicenseChannel.Unknown;
    }

    private static bool IsMatch(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, Timeout); }
        catch (ArgumentException) { return false; }          // invalid pattern
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static List<string> EvaluateSignatures(LicenseRaw raw, ISystemInfo sys, IReadOnlyList<CrackSignature> sigs)
    {
        var evidence = new List<string>();
        void Add(string e) { if (!evidence.Contains(e)) evidence.Add(e); }
        IReadOnlyList<string>? services = null;
        string? hosts = null;
        bool hostsRead = false;

        foreach (var sig in sigs)
        {
            switch (sig.Kind)
            {
                case "kms_host":
                    if (!string.IsNullOrEmpty(raw.KmsHost) && IsMatch(raw.KmsHost, sig.Pattern))
                        Add($"KMS host = {raw.KmsHost}");
                    break;
                case "service":
                    services ??= SafeServices(sys);
                    foreach (var name in services)
                        if (IsMatch(name, sig.Pattern)) Add($"Service: {name}");
                    break;
                case "file":
                    try
                    {
                        var path = Environment.ExpandEnvironmentVariables(sig.Pattern);
                        if (sys.PathExists(path)) Add($"File: {path}");
                    }
                    catch (Exception) { }
                    break;
                case "hosts_block":
                    if (!hostsRead)
                    {
                        hostsRead = true;
                        try { hosts = sys.ReadHostsFile(); } catch (Exception) { hosts = null; }
                    }
                    if (!string.IsNullOrEmpty(hosts) && IsMatch(hosts, sig.Pattern))
                        Add("File hosts chặn máy chủ kích hoạt");
                    break;
            }
        }
        return evidence;
    }

    private static IReadOnlyList<string> SafeServices(ISystemInfo sys)
    {
        try { return sys.ServiceNames(); } catch (Exception) { return Array.Empty<string>(); }
    }
}
