using System.Text;
using AutoDubbing.Service.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.API.Commands;

public static class CreateAdminCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Hãy chạy create-admin trong terminal tương tác để nhập mật khẩu ẩn.");
            return 1;
        }

        Console.WriteLine("Tạo Admin đầu tiên trong database đã cấu hình. Không chạy web server.");
        Console.Write("Username (phân biệt hoa/thường): ");
        var username = Console.ReadLine() ?? string.Empty;
        Console.Write("Họ tên: ");
        var fullName = Console.ReadLine() ?? string.Empty;
        Console.Write("Mật khẩu: ");
        var password = ReadPassword();
        Console.Write("Nhập lại mật khẩu: ");
        var confirmation = ReadPassword();
        if (password != confirmation)
        {
            Console.Error.WriteLine("Hai mật khẩu không khớp. Chưa tạo tài khoản.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminProvisioningService>();
        try
        {
            var created = await service.CreateFirstAdminAsync(username, fullName, password);
            Console.WriteLine(created
                ? "Đã tạo Admin đầu tiên với trạng thái Active."
                : "Database đã có tài khoản. Lệnh này chỉ tạo Admin đầu tiên.");
            return created ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException)
        {
            Console.Error.WriteLine("Không tạo được tài khoản. Kiểm tra kết nối và áp dụng migration mới trước khi chạy lại.");
            return 1;
        }
    }

    private static string ReadPassword()
    {
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }
}
