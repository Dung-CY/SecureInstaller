using Installer.Core.Platform;

namespace Installer.Core.Tests.Platform;

public class ComputerNameTests
{
    [Theory]
    [InlineData("lâm thao", "LamThao")]
    [InlineData("  HÒA  bình ", "HoaBinh")]
    [InlineData("Phòng Kế-toán 2", "PhongKeToan2")]
    [InlineData("đông anh", "DongAnh")]
    [InlineData("HoaBinh", "HoaBinh")]
    [InlineData("LAM THAO", "LamThao")]
    public void Unit_is_pascal_case_without_marks(string input, string expected) =>
        Assert.Equal(expected, ComputerName.NormalizeUnit(input));

    [Theory]
    [InlineData("pth-kt-qlvbdh01", "PTH-KT-QLVBDH01")]
    [InlineData("máy đơn vị 1", "MAY-DON-VI-1")]
    public void Computer_name_is_upper_case_without_marks(string input, string expected) =>
        Assert.Equal(expected, ComputerName.Normalize(input));

    [Theory]
    [InlineData("PTH-KT-QLVBDH01", true)]
    [InlineData("PTH-LAMTHAO-QLVBDH01", true)]   // > 15 chars: allowed, only warned about
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("-PTH", false)]
    [InlineData("PTH_01", false)]
    [InlineData("PTH 01", false)]
    public void Validation(string name, bool ok) => Assert.Equal(ok, ComputerName.Validate(name) is null);
}
