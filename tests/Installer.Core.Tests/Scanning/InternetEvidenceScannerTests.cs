using Installer.Core.Platform;
using Installer.Core.Scanning;

namespace Installer.Core.Tests.Scanning;

public class InternetEvidenceScannerTests
{
    private sealed class FakeEvents : IEventLogReader
    {
        public List<(string Log, EventRecordInfo Rec)> All { get; } = new();
        public IReadOnlyList<EventRecordInfo> Read(string logName, int[] ids, int max = 2000) =>
            All.Where(a => a.Log == logName && ids.Contains(a.Rec.Id)).Select(a => a.Rec).OrderByDescending(r => r.TimeCreated).ToList();
    }

    private static readonly DateTime T = new(2026, 11, 10, 8, 0, 0);

    private static EventRecordInfo Profile(string name, uint state, int days) =>
        new(10000, T.AddDays(days), new object?[] { name, "d", Guid.NewGuid(), 0u, state, 0u });

    private static EventRecordInfo Wu(int days) =>
        new(19, T.AddDays(days), new object?[] { "Update", Guid.NewGuid(), 200u }, "Microsoft-Windows-WindowsUpdateClient");

    private static EventRecordInfo Time(string peer, int days) =>
        new(37, T.AddDays(days), new object?[] { peer }, "Microsoft-Windows-Time-Service");

    [Fact]
    public void Internet_flag_gives_Yes_and_HadInternet_per_profile_name()
    {
        var ev = new FakeEvents();
        ev.All.Add(("Microsoft-Windows-NetworkProfile/Operational", Profile("Office", 0x40, -3)));
        ev.All.Add(("Microsoft-Windows-NetworkProfile/Operational", Profile("OFFICE", 0x20, -1)));
        ev.All.Add(("Microsoft-Windows-NetworkProfile/Operational", Profile("Lab", 0x20, -2)));
        var r = new InternetEvidenceScanner(ev).Scan();
        Assert.Equal(InternetVerdict.Yes, r.Evidence.Verdict);
        Assert.Equal(T.AddDays(-3), r.Evidence.LastSeen);
        Assert.True(r.ByProfile["office"]);
        Assert.False(r.ByProfile.ContainsKey("Lab"));   // no internet flag, no NCSI -> unknown, never "local only"
        Assert.False(r.ByProfile.ContainsKey("Other"));
    }

    [Fact]
    public void Profile_without_flag_or_ncsi_is_unknown_and_NoEvidence()
    {
        var ev = new FakeEvents();
        ev.All.Add(("Microsoft-Windows-NetworkProfile/Operational", Profile("Lab", 0x20, -2)));
        var r = new InternetEvidenceScanner(ev).Scan();
        Assert.False(r.ByProfile.ContainsKey("Lab"));
        Assert.Equal(InternetVerdict.NoEvidence, r.Evidence.Verdict);
        Assert.Null(r.Evidence.LastSeen);
    }

    private static EventRecordInfo Ncsi(DateTime t, uint cap) =>
        new(4042, t, new object?[] { Guid.NewGuid(), 1UL, 0u, cap, 4u, 0u });

    [Fact]
    public void Ncsi_after_connect_is_attributed_to_that_profile()
    {
        var ev = new FakeEvents();
        const string P = "Microsoft-Windows-NetworkProfile/Operational", N = "Microsoft-Windows-NCSI/Operational";
        ev.All.Add((P, Profile("Office", 9, -3)));
        ev.All.Add((N, Ncsi(T.AddDays(-3).AddSeconds(5), 1)));
        ev.All.Add((N, Ncsi(T.AddDays(-3).AddSeconds(8), 2)));
        ev.All.Add((P, Profile("Lab", 9, -2)));
        ev.All.Add((N, Ncsi(T.AddDays(-2).AddSeconds(5), 1)));
        ev.All.Add((P, Profile("Late", 9, -1)));
        ev.All.Add((N, Ncsi(T.AddDays(-1).AddMinutes(30), 2)));   // outside the probe window
        var r = new InternetEvidenceScanner(ev).Scan();
        Assert.True(r.ByProfile["Office"]);
        Assert.False(r.ByProfile["Lab"]);
        Assert.False(r.ByProfile.ContainsKey("Late"));
        Assert.Equal(InternetVerdict.Yes, r.Evidence.Verdict);
    }

    [Fact]
    public void Ncsi_internet_capability_gives_Yes()
    {
        var ev = new FakeEvents();
        ev.All.Add(("Microsoft-Windows-NCSI/Operational", new(4042, T, new object?[] { Guid.NewGuid(), 1UL, 0u, 2u, 4u, 0u })));
        ev.All.Add(("Microsoft-Windows-NCSI/Operational", new(4042, T.AddDays(1), new object?[] { Guid.NewGuid(), 1UL, 0u, 1u, 4u, 0u })));
        var r = new InternetEvidenceScanner(ev).Scan();
        Assert.Equal(InternetVerdict.Yes, r.Evidence.Verdict);
        Assert.Equal(T, r.Evidence.LastSeen);
    }

    [Fact]
    public void Only_windows_update_gives_Indirect()
    {
        var ev = new FakeEvents();
        ev.All.Add(("System", Wu(-5)));
        var r = new InternetEvidenceScanner(ev).Scan();
        Assert.Equal(InternetVerdict.Indirect, r.Evidence.Verdict);
        Assert.Equal(T.AddDays(-5), r.Evidence.LastSeen);
    }

    [Fact]
    public void Nothing_gives_NoEvidence()
    {
        var r = new InternetEvidenceScanner(new FakeEvents()).Scan();
        Assert.Equal(InternetVerdict.NoEvidence, r.Evidence.Verdict);
        Assert.Empty(r.Evidence.Items);
    }

    [Theory]
    [InlineData("192.168.1.5,0x8 (ntp.m|0x8|0.0.0.0:123->192.168.1.5:123)")]
    [InlineData("10.1.2.3,0x8")]
    [InlineData("172.20.0.1,0x8")]
    [InlineData("169.254.1.1,0x8")]
    [InlineData("fe80::1,0x8")]
    [InlineData("fd00::1,0x8")]
    [InlineData("127.0.0.1,0x8")]
    [InlineData("VMICTimeProvider")]
    public void Private_time_peer_is_ignored(string peer)
    {
        var ev = new FakeEvents();
        ev.All.Add(("System", Time(peer, 0)));
        Assert.Equal(InternetVerdict.NoEvidence, new InternetEvidenceScanner(ev).Scan().Evidence.Verdict);
    }

    [Theory]
    [InlineData("time.windows.com,0x9 (ntp.m|0x9|0.0.0.0:123->20.189.79.72:123)")]
    [InlineData("20.189.79.72,0x8")]
    public void Public_time_peer_gives_Indirect(string peer)
    {
        var ev = new FakeEvents();
        ev.All.Add(("System", Time(peer, 0)));
        Assert.Equal(InternetVerdict.Indirect, new InternetEvidenceScanner(ev).Scan().Evidence.Verdict);
    }

    [Fact]
    public void Strong_evidence_wins_over_indirect_and_LastSeen_comes_from_strong()
    {
        var ev = new FakeEvents();
        ev.All.Add(("System", Wu(0)));
        ev.All.Add(("Microsoft-Windows-NetworkProfile/Operational", Profile("Office", 0x400, -7)));
        var r = new InternetEvidenceScanner(ev).Scan();
        Assert.Equal(InternetVerdict.Yes, r.Evidence.Verdict);
        Assert.Equal(T.AddDays(-7), r.Evidence.LastSeen);
    }
}
