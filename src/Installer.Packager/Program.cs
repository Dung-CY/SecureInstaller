using System.Text;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.Packager;

public static class Program
{
    private const string Usage = """
        Cách dùng:
          SecureInstaller.Packager init <root> --user <admin> [--password <pw>]
          SecureInstaller.Packager add-package <root> <folder> --user <u> [--password <pw>]
          SecureInstaller.Packager update-package <root> <id> <folder> [--keep-old] --user <u> [--password <pw>]
          SecureInstaller.Packager remove-package <root> <id> [--version <v>] --user <u> [--password <pw>]
          SecureInstaller.Packager add-user <root> <name> --role user|admin --new-password <pw> --user <u> [--password <pw>]
          SecureInstaller.Packager remove-user <root> <name> --user <u> [--password <pw>]
          SecureInstaller.Packager export-logs <root> <out.csv> --user <u> [--password <pw>]
        """;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return Run(args);
    }

    public static int Run(string[] argv)
    {
        try
        {
            var p = Args.Parse(argv);
            return Execute(p);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (CatalogCommitPendingException)
        {
            Console.Error.WriteLine("Thay đổi đã ghi một phần; hãy chạy lại lệnh bất kỳ để hoàn tất.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Execute(ParsedArgs p)
    {
        var pos = p.Positionals;
        void Need(int n)
        {
            if (pos.Count != n) throw new ArgumentException($"Lệnh {p.Command} cần {n} tham số vị trí.");
        }
        string User() => Opt(p, "user") ?? throw new ArgumentException("Thiếu --user.");

        switch (p.Command)
        {
            case "init":
            {
                Need(1);
                var user = User();
                var pw = Opt(p, "password") ?? PromptPassword("Mật khẩu quản trị mới: ", confirm: true);
                VaultStore.Create(pos[0], user, pw);
                Console.WriteLine($"Đã tạo kho tại {pos[0]}");
                return 0;
            }
            case "add-package":
            {
                Need(2);
                var admin = OpenAdmin(p, pos[0]);
                var e = admin.AddPackage(pos[1]);
                Console.WriteLine($"Đã thêm gói {e.Id} {e.Version}");
                return 0;
            }
            case "update-package":
            {
                Need(3);
                var keepOld = p.Options.ContainsKey("keep-old");
                var admin = OpenAdmin(p, pos[0]);
                var e = admin.UpdatePackage(pos[1], pos[2], keepOld);
                Console.WriteLine($"Đã cập nhật gói {e.Id} {e.Version}");
                return 0;
            }
            case "remove-package":
            {
                Need(2);
                var admin = OpenAdmin(p, pos[0]);
                admin.RemovePackage(pos[1], Opt(p, "version"));
                Console.WriteLine($"Đã xóa gói {pos[1]}");
                return 0;
            }
            case "add-user":
            {
                Need(2);
                var roleText = Opt(p, "role") ?? throw new ArgumentException("Thiếu --role.");
                var role = roleText.ToLowerInvariant() switch
                {
                    "user" => UserRole.User,
                    "admin" => UserRole.Admin,
                    _ => throw new ArgumentException("--role chỉ nhận user hoặc admin.")
                };
                var admin = OpenAdmin(p, pos[0]);
                var npw = Opt(p, "new-password") ?? PromptPassword("Mật khẩu cho người dùng mới: ", confirm: false);
                admin.AddUser(pos[1], npw, role);
                Console.WriteLine($"Đã thêm người dùng {pos[1]}");
                return 0;
            }
            case "remove-user":
            {
                Need(2);
                var admin = OpenAdmin(p, pos[0]);
                admin.RemoveUser(pos[1]);
                Console.WriteLine($"Đã xóa người dùng {pos[1]}");
                return 0;
            }
            case "export-logs":
                Need(2);
                User();
                Console.Error.WriteLine("export-logs: chưa hỗ trợ");
                return 2;
            default:
                throw new ArgumentException($"Lệnh không hợp lệ: {p.Command}");
        }
    }

    private static string? Opt(ParsedArgs p, string name) =>
        p.Options.TryGetValue(name, out var v) ? v : null;

    private static VaultAdmin OpenAdmin(ParsedArgs p, string root)
    {
        var user = Opt(p, "user") ?? throw new ArgumentException("Thiếu --user.");
        var pw = Opt(p, "password") ?? PromptPassword("Mật khẩu: ", confirm: false);
        var store = VaultStore.Open(root);
        var session = store.Unlock(user, pw, DateTimeOffset.UtcNow);
        return new VaultAdmin(session);
    }

    private static string PromptPassword(string prompt, bool confirm)
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine() ?? throw new ArgumentException("Không đọc được mật khẩu từ stdin.");
        var a = ReadHidden(prompt);
        if (confirm && ReadHidden("Nhập lại mật khẩu: ") != a)
            throw new ArgumentException("Hai mật khẩu không khớp.");
        return a;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Error.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (k.KeyChar != '\0') sb.Append(k.KeyChar);
        }
        Console.Error.WriteLine();
        return sb.ToString();
    }
}
