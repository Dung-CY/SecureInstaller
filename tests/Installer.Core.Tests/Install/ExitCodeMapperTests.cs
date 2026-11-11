using Installer.Core.Install;

namespace Installer.Core.Tests.Install;

public class ExitCodeMapperTests
{
    [Theory]
    [InlineData(0, InstallStatus.Success)]
    [InlineData(3010, InstallStatus.RebootRequired)]
    [InlineData(1641, InstallStatus.RebootRequired)]
    [InlineData(1602, InstallStatus.Failed)]
    [InlineData(1618, InstallStatus.Failed)]
    [InlineData(1603, InstallStatus.Failed)]
    public void Map_codes(int c, InstallStatus s) => Assert.Equal(s, ExitCodeMapper.Map(c).Status);

    [Fact]
    public void Map_unknown_message_contains_code()
        => Assert.Equal("Lỗi cài đặt (mã 1603)", ExitCodeMapper.Map(1603).Message);

    [Fact]
    public void Map_fixed_messages()
    {
        Assert.Equal("Cài đặt thành công", ExitCodeMapper.Map(0).Message);
        Assert.Equal("Cần khởi động lại máy", ExitCodeMapper.Map(3010).Message);
        Assert.Equal("Người dùng hủy cài đặt", ExitCodeMapper.Map(1602).Message);
        Assert.Equal("Đang có một bộ cài khác chạy", ExitCodeMapper.Map(1618).Message);
    }
}

public class MsiProgressParserTests
{
    [Fact]
    public void Forward_reset_then_reports_increase()
    {
        var p = new MsiProgressParser();
        Assert.Null(p.Handle("1: 0 2: 100 3: 0 4: 1"));
        Assert.Equal(10, p.Handle("1: 2 2: 10 3: 0 4: 0"));
        Assert.Equal(30, p.Handle("1: 2 2: 20 3: 0 4: 0"));
    }

    [Fact]
    public void Add_total_lowers_percent_so_no_report()
    {
        var p = new MsiProgressParser();
        p.Handle("1: 0 2: 100 3: 0 4: 1");
        Assert.Equal(50, p.Handle("1: 2 2: 50 3: 0 4: 0"));
        Assert.Null(p.Handle("1: 3 2: 100 3: 0 4: 0")); // 50/200 = 25
        Assert.Equal(75, p.Handle("1: 2 2: 100 3: 0 4: 0")); // 150/200
    }

    [Fact]
    public void Backward_reports_decrease_as_null()
    {
        var p = new MsiProgressParser();
        p.Handle("1: 0 2: 100 3: 1 4: 1"); // backward, position = 100 -> 100%
        Assert.Null(p.Handle("1: 2 2: 30 3: 0 4: 0")); // position 70 -> lower, ignored
    }

    [Fact]
    public void Garbage_ignored()
    {
        var p = new MsiProgressParser();
        Assert.Null(p.Handle("hello"));
        Assert.Null(p.Handle(""));
        Assert.Null(p.Handle("1: x 2: y"));
    }
}
