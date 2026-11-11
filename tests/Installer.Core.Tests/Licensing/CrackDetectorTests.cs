using Installer.Core.Licensing;
using Installer.Core.Tests.Fakes;

namespace Installer.Core.Tests.Licensing;

public class CrackDetectorTests
{
    private static LicenseRaw Raw(string? channel = "OEM:DM", int? status = 1, string? kms = null, string? desc = null) =>
        new("Windows 11 Pro", "Windows(R) Operating System", channel, desc, status, "ABCDE", kms);

    private static LicenseInfo Eval(LicenseRaw? raw, FakeSystemInfo? sys = null, bool office = false) =>
        CrackDetector.Evaluate(raw, sys ?? new FakeSystemInfo(), CrackSignatureDefaults.All, office);

    [Fact]
    public void Oem_activated_is_Genuine()
    {
        var r = Eval(Raw());
        Assert.Equal(LicenseVerdict.Genuine, r.Verdict);
        Assert.Equal(LicenseChannel.Oem, r.Channel);
        Assert.Empty(r.Evidence);
    }

    [Fact]
    public void Kms_loopback_is_CrackSuspected_with_evidence()
    {
        var r = Eval(Raw(channel: "VOLUME_KMSCLIENT", kms: "127.0.0.2"));
        Assert.Equal(LicenseVerdict.CrackSuspected, r.Verdict);
        Assert.Contains("KMS host = 127.0.0.2", r.Evidence);
    }

    [Fact]
    public void Known_service_is_CrackSuspected()
    {
        var sys = new FakeSystemInfo();
        sys.Services.Add("KMSELDI");
        var r = Eval(Raw(), sys);
        Assert.Equal(LicenseVerdict.CrackSuspected, r.Verdict);
        Assert.Contains("Service: KMSELDI", r.Evidence);
    }

    [Fact]
    public void Hosts_block_is_CrackSuspected()
    {
        var sys = new FakeSystemInfo { HostsFile = "127.0.0.1 localhost\r\n0.0.0.0 activation-v2.sls.microsoft.com\r\n" };
        var r = Eval(Raw(), sys);
        Assert.Equal(LicenseVerdict.CrackSuspected, r.Verdict);
        Assert.Contains("File hosts chặn máy chủ kích hoạt", r.Evidence);
    }

    [Fact]
    public void Not_activated_is_Suspicious()
    {
        var r = Eval(Raw(status: 0));
        Assert.Equal(LicenseVerdict.Suspicious, r.Verdict);
        Assert.Contains("Chưa kích hoạt", r.Evidence);
    }

    [Fact]
    public void Gvlk_not_domain_joined_is_Suspicious_but_domain_joined_is_Genuine()
    {
        var raw = Raw(channel: "VOLUME_KMSCLIENT", kms: "kms.corp.local");
        var a = Eval(raw, new FakeSystemInfo { IsDomainJoined = false });
        Assert.Equal(LicenseVerdict.Suspicious, a.Verdict);
        Assert.Contains("Key KMS trên máy không thuộc domain", a.Evidence);
        var b = Eval(raw, new FakeSystemInfo { IsDomainJoined = true });
        Assert.Equal(LicenseVerdict.Genuine, b.Verdict);
    }

    [Fact]
    public void Null_raw_windows_Unknown_office_NotInstalled()
    {
        var w = Eval(null);
        Assert.Equal(LicenseVerdict.Unknown, w.Verdict);
        Assert.Contains("Không đọc được thông tin bản quyền", w.Evidence);
        Assert.Equal(LicenseVerdict.NotInstalled, Eval(null, office: true).Verdict);
    }
}
