# Đăng nhập Admin

Backend gồm hai API:

| API | Truy cập | Kết quả |
| --- | --- | --- |
| `POST /api/admin/auth/login` | Không cần token | JWT và thông tin Admin |
| `GET /api/admin/auth/me` | `Authorization: Bearer <token>` | Thông tin Admin đang hoạt động |

Mọi bản ghi `NGUOI_DUNG` là Admin, không có cột vai trò. Không có API đăng ký công khai, không seed tài khoản khi khởi động. Response chỉ có `id`, `username`, `fullName`, `status`; không serialize entity hoặc `PasswordHash`.

Username duy nhất, tối đa 100 ký tự UTF-16, **phân biệt hoa/thường**: `Admin` và `admin` là hai tài khoản khác nhau. Cột username dùng collation `Latin1_General_100_BIN2` và unique index. Input không được rỗng hoặc có khoảng trắng đầu/cuối; hệ thống không tự đổi hoa/thường hay trim username. Mật khẩu không bị trim, không được rỗng/chỉ có khoảng trắng; giới hạn input 1024 ký tự.

Trạng thái chỉ gồm `Active` và `Locked`, có check constraint tại database. Chỉ `Active` được đăng nhập. Mỗi request dùng JWT tới API được bảo vệ kiểm tra lại tài khoản trong database; tài khoản bị khóa hoặc bị xóa bị trả `401` ngay cả khi token chưa hết hạn. Khi tài khoản được mở khóa, token cũ còn hạn có thể dùng lại. Chưa có refresh token hoặc cơ chế thu hồi từng token.

## 1. Build và kiểm thử trước khi thay đổi database

Chạy tại thư mục `E:\AutoDubbingSystem`:

```powershell
dotnet restore AutoDubbingSystem.slnx
dotnet build AutoDubbingSystem.slnx --no-restore
dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore
```

Test dùng SQLite in-memory, thay thế hoàn toàn đăng ký SQL Server trong test host. Cột JSON lộ trình được ánh xạ sang `TEXT` riêng trong test; collation so sánh ordinal và hàm `DATALENGTH` được đăng ký cho SQLite. Test host dùng console logger để không phụ thuộc quyền Windows Event Log. Mật khẩu và signing key của test được sinh ngẫu nhiên. Test không ghi vào `AutoDubbingDb`, không cần key thật hoặc tài khoản thật.

## 2. Cấu hình signing key bằng User Secrets trên máy của bạn

`Issuer`, `Audience` và thời hạn mặc định 30 phút nằm trong `appsettings.json`. Signing key **không có giá trị mặc định**; API từ chối khởi động nếu key thiếu, không đúng Base64 hoặc giải mã ra dưới 32 byte. Không gửi key hoặc mật khẩu vào cuộc trò chuyện.

Sinh key ngẫu nhiên 64 byte rồi lưu vào User Secrets. Chạy các dòng sau trong terminal PowerShell tại workspace; không in key ra màn hình:

```powershell
$jwtKeyBytes = New-Object byte[] 64
$jwtRandom = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtRandom.GetBytes($jwtKeyBytes)
$jwtRandom.Dispose()
$jwtSigningKey = [Convert]::ToBase64String($jwtKeyBytes)
@{ 'Jwt:SigningKey' = $jwtSigningKey } | ConvertTo-Json | dotnet user-secrets set --project AutoDubbing.API
Remove-Variable jwtSigningKey, jwtKeyBytes, jwtRandom
```

Kết quả mong đợi là thông báo lưu thành công 1 secret. Project đã có `UserSecretsId`, không cần chạy `user-secrets init`. Không chạy `user-secrets list` để gửi kết quả vì lệnh đó in key ra màn hình.

User Secrets được nạp khi môi trường là `Development`; profile `https` trong dự án đã thiết lập môi trường này. Khi chạy ở môi trường khác, cấp key qua cấu hình bên ngoài, ví dụ biến môi trường `Jwt__SigningKey`. Không ghi key vào source hoặc image Docker. Xem [hướng dẫn User Secrets của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

JWT dùng HS256. API kiểm tra chữ ký, thuật toán, issuer, audience, `exp`, `nbf` và subject; không có khoảng dung sai thời gian. Response đăng nhập và thông tin Admin dùng `Cache-Control: no-store`. Tham khảo [tài liệu JWT bearer của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).

## 3. Xem và áp dụng migration mới trên máy của bạn

`InitialCreate` được giữ nguyên. Migration mới `ConfigureAdminAuthentication` giới hạn độ dài username, đặt collation, tạo unique index và ràng buộc trạng thái.

Xem SQL trước khi áp dụng:

```powershell
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
dotnet ef migrations script InitialCreate ConfigureAdminAuthentication --project AutoDubbing.Data --startup-project AutoDubbing.API --output artifacts/admin-auth.sql
```

Khi bạn sẵn sàng cập nhật schema của `AutoDubbingDb`:

```powershell
dotnet ef database update --project AutoDubbing.Data --startup-project AutoDubbing.API
```

Connection string hiện tại dùng SQL Server `.`, Windows Authentication, database `AutoDubbingDb`. API không tự chạy migration. Migration mới từ chối dữ liệu username quá dài/trùng theo collation mới hoặc trạng thái ngoài `Active`/`Locked`; không tự sửa, xóa hay cắt ngắn dữ liệu cũ. Nếu có dữ liệu không hợp lệ, cần thống nhất cách xử lý trước khi cập nhật schema.

Sau khi lệnh cập nhật migration thành công, xác minh **metadata thật trên SQL Server** bằng script chỉ đọc:

```powershell
sqlcmd -S . -d AutoDubbingDb -E -C -b -i docs/verify-admin-auth.sql
```

Hoặc mở `docs/verify-admin-auth.sql` trong SSMS, kết nối server `.`, chọn `AutoDubbingDb` rồi thực thi. Kết quả mong đợi: username là `nvarchar`, `max_length_bytes = 200` (100 đơn vị UTF-16), collation `Latin1_General_100_BIN2`, index `is_unique = 1`, `is_disabled = 0`, cuối cùng có dòng `PASS`. Script báo lỗi nếu migration/ràng buộc còn thiếu; không đọc mật khẩu/hash và không sửa dữ liệu.

Collation được đặt **trên cột**, nên phép tìm username và unique index cùng phân biệt hoa/thường, dù collation mặc định của database là `CI`. Theo [tài liệu SQL Server](https://learn.microsoft.com/en-us/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver17), `BIN2` so sánh Unicode theo code point. SQL Server vẫn có quy tắc padding khoảng trắng riêng; Service hiện từ chối khoảng trắng đầu/cuối username.

## 4. Tự tạo Admin đầu tiên

Sau khi migration mới được áp dụng, chạy trong terminal tương tác trên máy của bạn:

```powershell
dotnet run --project AutoDubbing.API -- create-admin
```

Nhập username, họ tên, mật khẩu và xác nhận mật khẩu. Mật khẩu được nhập ẩn, không truyền qua command line, không ghi log. Không gửi mật khẩu vào cuộc trò chuyện hoặc đặt trong file source/HTTP request được commit.

Lệnh dùng `PasswordHasher<AdminUser>` của ASP.NET Core để lưu hash có salt và tạo tài khoản `Active`. Lệnh chỉ thành công khi bảng tài khoản đang trống; kiểm tra và ghi trong transaction `Serializable`. Lệnh này không chạy web server, không cần signing key JWT và không tạo POI hay dữ liệu mẫu.

## 5. Chạy API và gọi thử bằng tài khoản của bạn

```powershell
dotnet run --project AutoDubbing.API --launch-profile https
```

Nếu chứng chỉ HTTPS phát triển chưa được tin cậy, thực hiện `dotnet dev-certs https --trust` trên máy của bạn. Mở terminal PowerShell khác, nhập thông tin cục bộ rồi gọi API:

```powershell
$adminUsername = Read-Host 'Username (phân biệt hoa/thường)'
$adminPassword = Read-Host 'Mật khẩu' -AsSecureString
$adminCredential = New-Object System.Management.Automation.PSCredential($adminUsername, $adminPassword)
$loginBody = @{
    username = $adminUsername
    password = $adminCredential.GetNetworkCredential().Password
} | ConvertTo-Json
$login = Invoke-RestMethod -Method Post -Uri 'https://localhost:7239/api/admin/auth/login' -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($loginBody))
Remove-Variable loginBody, adminCredential, adminPassword
$login.admin
Invoke-RestMethod -Uri 'https://localhost:7239/api/admin/auth/me' -Headers @{ Authorization = "Bearer $($login.accessToken)" }
# Giữ $login trong phiên PowerShell này để kiểm tra khóa tài khoản ở bước 6.
```

Không in hoặc chia sẻ biến `$login` vì có chứa token. Hình dạng response đăng nhập:

```json
{
  "accessToken": "<JWT được cấp>",
  "tokenType": "Bearer",
  "expiresAtUtc": "<thời điểm UTC>",
  "admin": {
    "id": 1,
    "username": "<username của bạn>",
    "fullName": "<họ tên>",
    "status": "Active"
  }
}
```

`400`: payload thiếu trường/bị vượt giới hạn. `401` khi đăng nhập: thông tin sai hoặc tài khoản không hoạt động, dùng cùng thông báo để không tiết lộ tài khoản tồn tại/bị khóa. `401` khi gọi API được bảo vệ: thiếu token, token không hợp lệ/hết hạn hoặc tài khoản không còn hoạt động.

## 6. Kiểm tra bốn tình huống trên SQL Server bằng tài khoản bạn vừa tạo

Có thể chạy công cụ tự động thay cho các lệnh thủ công bên dưới:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File scripts/verify-admin-login.ps1 -TrustDevelopmentCertificate -IncludeLockTest
```

Công cụ mở hộp nhập tài khoản cục bộ, không yêu cầu gửi mật khẩu vào chat. Nó kiểm tra thiếu token, token giả, login đúng, `/me`, sai mật khẩu, login và `/me` khi Locked, rồi `/me` sau khi khôi phục Active. Chỉ dùng tài khoản hiện có, không tạo tài khoản mẫu và không đổi mật khẩu. `-IncludeLockTest` cho phép tạm đổi trạng thái đúng tài khoản đăng nhập trong `AutoDubbingDb`; `finally` khôi phục Active. Giữ cửa sổ mở đến khi hoàn tất. Nếu tiến trình bị đóng cưỡng bức hoặc SQL Server mất kết nối, cần kiểm tra trạng thái và khôi phục Active theo ID trong báo cáo lỗi.

Báo cáo tại `artifacts/test-results/admin-auth-sqlserver.json` chỉ chứa tên ca, mã HTTP, trạng thái hoàn tất/khôi phục, thời gian và giai đoạn lỗi; không chứa mật khẩu, hash, key, JWT hoặc response body. Script không đọc signing key; API dùng cấu hình local hiện có. Nếu không dùng công cụ, thực hiện các bước thủ công tiếp theo.

Thực hiện sau bước 5 trong cùng terminal, giữ API đang chạy. Hai lệnh thành công ở bước 5 xác nhận login và `/me` đều trả `200`. Chỉ chia sẻ mã HTTP/kết quả kiểm tra; không chia sẻ `$login`, request body, token, key hoặc mật khẩu.

Khai báo helper lấy mã HTTP, dùng được trên Windows PowerShell và PowerShell 7:

```powershell
function Get-AdminStatus {
    param([string]$Method, [string]$Path, [string]$JsonBody, [hashtable]$Headers = @{})
    $request = @{
        Uri = "https://localhost:7239/api/admin/auth/$Path"
        Method = $Method
        Headers = $Headers
        UseBasicParsing = $true
        ErrorAction = 'Stop'
    }
    if ($JsonBody) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [System.Text.Encoding]::UTF8.GetBytes($JsonBody)
    }
    try { [int](Invoke-WebRequest @request).StatusCode }
    catch {
        if ($null -ne $_.Exception.Response) { [int]$_.Exception.Response.StatusCode }
        else { throw }
    }
}
```

Sai mật khẩu: kỳ vọng `401`.

```powershell
$wrongBody = @{ username = $adminUsername; password = [Guid]::NewGuid().ToString('N') } | ConvertTo-Json
Get-AdminStatus -Method POST -Path login -JsonBody $wrongBody
Remove-Variable wrongBody
```

Thiếu token khi gọi `/me`: kỳ vọng `401`.

```powershell
Get-AdminStatus -Method GET -Path me
```

Tài khoản bị khóa: bạn tự chạy lệnh SQL sau để khóa **đúng ID từ lần login thành công**, không tạo tài khoản khác. Lệnh này sửa trạng thái của tài khoản thật; chỉ thực hiện khi sẵn sàng kiểm tra ca khóa.

```powershell
if ($null -eq $login -or $login.admin.id -le 0) { throw 'Hãy thực hiện login thành công ở bước 5 trước.' }
$adminId = [int]$login.admin.id
sqlcmd -S . -d AutoDubbingDb -E -C -b -Q "SET NOCOUNT ON; UPDATE dbo.NGUOI_DUNG SET trang_thai=N'Locked' WHERE id=$adminId; SELECT id,trang_thai FROM dbo.NGUOI_DUNG WHERE id=$adminId;"
Get-AdminStatus -Method GET -Path me -Headers @{ Authorization = "Bearer $($login.accessToken)" }
```

Kết quả SQL phải hiện đúng ID và `Locked`; `/me` dùng token đã cấp phải trả `401`. Tiếp tục thử đăng nhập bằng **mật khẩu đúng**, nhập lại cục bộ, kỳ vọng `401`:

```powershell
$lockedPassword = Read-Host 'Mật khẩu đúng của tài khoản đang Locked' -AsSecureString
$lockedCredential = New-Object System.Management.Automation.PSCredential($adminUsername, $lockedPassword)
$lockedBody = @{ username = $adminUsername; password = $lockedCredential.GetNetworkCredential().Password } | ConvertTo-Json
Get-AdminStatus -Method POST -Path login -JsonBody $lockedBody
Remove-Variable lockedBody, lockedCredential, lockedPassword
```

Kết thúc ca khóa, bạn tự đưa tài khoản về `Active` để tiếp tục demo:

```powershell
sqlcmd -S . -d AutoDubbingDb -E -C -b -Q "SET NOCOUNT ON; UPDATE dbo.NGUOI_DUNG SET trang_thai=N'Active' WHERE id=$adminId; SELECT id,trang_thai FROM dbo.NGUOI_DUNG WHERE id=$adminId;"
Remove-Variable login, adminUsername, adminId
```

Nếu gặp lỗi kết nối/chứng chỉ, đó chưa phải kết quả HTTP của API; giữ nguyên lỗi để chẩn đoán, không bỏ qua xác minh chứng chỉ. Khi báo kết quả, chỉ cần: `migration PASS; create-admin thành công; login 200; me 200; sai mật khẩu 401; thiếu token 401; Locked login/me 401; đã mở khóa lại`.

## Phân chia source

- `AutoDubbing.API/Controllers/AdminAuthController.cs`: HTTP đăng nhập và thông tin hiện tại.
- `AutoDubbing.API/Authentication/`: cấu hình, phát và xác minh JWT; kiểm tra tài khoản sau khi token được xác minh.
- `AutoDubbing.API/Commands/CreateAdminCommand.cs`: giao diện console tạo Admin đầu tiên.
- `AutoDubbing.API/Program.cs`: đăng ký dependency và middleware, lựa chọn chế độ console/web.
- `AutoDubbing.Service/Authentication/`: nghiệp vụ đăng nhập, DTO, kiểm tra trạng thái, băm mật khẩu và tạo tài khoản đầu tiên.
- `AutoDubbing.Data/Repositories/`: truy cập database và transaction tạo Admin đầu tiên.
- `AutoDubbing.Data/Entities/AdminAccountStatus.cs`, `AdminUser.cs`, `Contexts/AppDbContext.cs`, migration mới và snapshot: trạng thái và ràng buộc dữ liệu.
- `AutoDubbing.Tests/`: test HTTP với middleware JWT thật và database riêng.
- Hai csproj API/Service: package xác thực; `AutoDubbingSystem.slnx` thêm test project; `Dockerfile` copy csproj test để bước restore solution vẫn hoạt động.

Package xác thực/EF Core mới dùng bản `10.0.13`, cùng dòng .NET/EF Core hiện có. Bước xác minh bổ sung ghim riêng dependency gián tiếp `System.Security.Cryptography.Pkcs` lên bản vá `9.0.21` tại Data; không thay nghiệp vụ đăng nhập hoặc migration.

## Danh sách file thay đổi

Đường dẫn tính từ workspace; tính cả công cụ xác minh local: **10 file sửa, 23 file mới**.

| Trạng thái | File |
| --- | --- |
| Sửa | `AutoDubbing.API/AutoDubbing.API.csproj` |
| Sửa | `AutoDubbing.API/Program.cs` |
| Sửa | `AutoDubbing.API/appsettings.json` |
| Sửa | `AutoDubbing.Data/AutoDubbing.Data.csproj` |
| Sửa | `AutoDubbing.Data/Contexts/AppDbContext.cs` |
| Sửa | `AutoDubbing.Data/Entities/AdminUser.cs` |
| Sửa | `AutoDubbing.Data/Migrations/AppDbContextModelSnapshot.cs` |
| Sửa | `AutoDubbing.Service/AutoDubbing.Service.csproj` |
| Sửa | `AutoDubbingSystem.slnx` |
| Sửa | `Dockerfile` |
| Mới | `AutoDubbing.API/Authentication/AuthenticationRegistration.cs` |
| Mới | `AutoDubbing.API/Authentication/JwtOptions.cs` |
| Mới | `AutoDubbing.API/Authentication/JwtTokenIssuer.cs` |
| Mới | `AutoDubbing.API/Commands/CreateAdminCommand.cs` |
| Mới | `AutoDubbing.API/Controllers/AdminAuthController.cs` |
| Mới | `AutoDubbing.Data/Entities/AdminAccountStatus.cs` |
| Mới | `AutoDubbing.Data/Migrations/20261019172708_ConfigureAdminAuthentication.cs` |
| Mới | `AutoDubbing.Data/Migrations/20261019172708_ConfigureAdminAuthentication.Designer.cs` |
| Mới | `AutoDubbing.Data/Repositories/AdminUserRepository.cs` |
| Mới | `AutoDubbing.Data/Repositories/IAdminUserRepository.cs` |
| Mới | `AutoDubbing.Service/Authentication/AdminAuthenticationService.cs` |
| Mới | `AutoDubbing.Service/Authentication/AdminLoginRequest.cs` |
| Mới | `AutoDubbing.Service/Authentication/AdminProfile.cs` |
| Mới | `AutoDubbing.Service/Authentication/AdminProvisioningService.cs` |
| Mới | `AutoDubbing.Service/Authentication/IAccessTokenIssuer.cs` |
| Mới | `AutoDubbing.Service/Authentication/IAdminAuthenticationService.cs` |
| Mới | `AutoDubbing.Service/Authentication/IAdminProvisioningService.cs` |
| Mới | `AutoDubbing.Tests/AdminApiFactory.cs` |
| Mới | `AutoDubbing.Tests/AdminAuthenticationTests.cs` |
| Mới | `AutoDubbing.Tests/AutoDubbing.Tests.csproj` |
| Mới | `docs/admin-auth.md` |
| Mới | `docs/verify-admin-auth.sql` |
| Mới | `scripts/verify-admin-login.ps1` |

SQL sinh ra và kết quả test nằm trong `artifacts/`, được Git bỏ qua.

## Kết quả lần bàn giao đầu — 20/10/2026

| Kiểm tra | Kết quả thật |
| --- | --- |
| `dotnet restore AutoDubbingSystem.slnx` | Thành công sau khi được cấp quyền truy cập NuGet |
| `dotnet build AutoDubbingSystem.slnx --no-restore` | Thành công; 0 lỗi, 8 cảnh báo NU1902/NU1903 |
| `dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=admin-auth.trx' --results-directory artifacts/test-results` | 33/33 đạt; 0 lỗi, 0 bỏ qua; lần cuối chạy trong sandbox |
| Sinh SQL từ `InitialCreate` đến `ConfigureAdminAuthentication` | Thành công; đã xem SQL trong `artifacts/admin-auth.sql` |
| `dotnet ef migrations has-pending-model-changes --project AutoDubbing.Data --startup-project AutoDubbing.API --no-build` | Không có thay đổi model chưa đưa vào migration |
| Kiểm tra Git với hai file `InitialCreate` | Không có thay đổi |

33 ca kiểm thử gồm đăng nhập và lấy thông tin thành công; response không chứa password/hash; sai mật khẩu, username không tồn tại, tài khoản khóa, hash hỏng; thiếu token, token sai định dạng/chữ ký/issuer/audience/thuật toán; token hết hạn, chưa tới hạn hoặc thiếu expiry/subject; subject sai và tài khoản không tồn tại; khóa tài khoản sau khi cấp token; username phân biệt hoa/thường, unique, giới hạn 100 ký tự; payload sai; trạng thái ngoài danh sách; nâng cấp hash Identity cũ; tạo duy nhất Admin đầu tiên bằng service; không seed/không có endpoint đăng ký; từ chối khởi động với signing key thiếu/hỏng/quá ngắn.

Lần test đầu phát hiện cấu hình SQLite không hiểu `nvarchar(max)` và lỗi quyền logger Windows che lỗi gốc. Đã sửa riêng test host rồi build và chạy lại toàn bộ; kết quả cuối là 33/33 đạt.

Các cảnh báo NU1902/NU1903 ở lần bàn giao đầu liên quan đến dependency có sẵn `System.Security.Cryptography.Pkcs 9.0.11`. Bước xác minh bổ sung bên dưới xử lý bản vá này.

Chưa kiểm tra bằng tài khoản thật trên SQL Server, chưa chạy migration mới vào `AutoDubbingDb`, chưa thử nhập mật khẩu tương tác để tạo tài khoản thật. Test SQLite không thay thế việc xác nhận migration trên SQL Server. Chưa chạy Docker build hoặc kiểm tra CI từ xa; chưa xác định nguyên nhân CI chờ/log trống. Không tạo dữ liệu kiểm thử trong database dự án; không commit, push hoặc merge.

## Xác minh bổ sung dependency và SQL Server — 20/10/2026

Đã dùng các lệnh sau để truy vết, không thay cấu hình audit:

```powershell
dotnet nuget why AutoDubbing.Data/AutoDubbing.Data.csproj System.Security.Cryptography.Pkcs
dotnet nuget why AutoDubbing.API/AutoDubbing.API.csproj System.Security.Cryptography.Pkcs
```

Chuỗi trước bản vá: `API → Service → Data → Microsoft.EntityFrameworkCore.SqlServer 10.0.13 → Microsoft.Data.SqlClient 6.1.7 → System.Security.Cryptography.Pkcs 9.0.11`.

| Advisory từ log | Vấn đề | Bản vá cùng dòng 9.x |
| --- | --- | --- |
| [GHSA-2fmv-rv2c-59x7 / CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) | DoS mức Moderate khi xử lý CMS/PKCS#7 trên Windows | `9.0.21` |
| [GHSA-846f-f764-76mx / CVE-2026-84008](https://github.com/advisories/GHSA-846f-f764-76mx) | DoS mức High khi xác minh chữ ký CMS | `9.0.21` |

Cả hai advisory đánh dấu `9.0.0–9.0.20` bị ảnh hưởng. Chọn [Pkcs 9.0.21](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/9.0.21), giữ cùng major, tương thích .NET 10 và thỏa dependency tối thiểu của SqlClient. Chỉ thêm một `PackageReference` tại `AutoDubbing.Data.csproj`; không nâng đồng loạt EF/SqlClient. Sau restore, `dotnet nuget why` và assets của cả bốn project đều xác nhận `9.0.21`. Không tắt NuGet Audit, không thêm `NoWarn`/audit suppression.

Kết quả chạy lại sau bản vá:

| Kiểm tra | Kết quả thật |
| --- | --- |
| `dotnet restore AutoDubbingSystem.slnx --force-evaluate` | Thành công |
| `dotnet build AutoDubbingSystem.slnx --no-restore` | Thành công; **0 lỗi, 0 cảnh báo** |
| `dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=admin-auth-pkcs-patch.trx' --results-directory artifacts/test-results` | **33/33 đạt**, 0 bỏ qua; report `artifacts/test-results/admin-auth-pkcs-patch.trx` |
| `dotnet list AutoDubbingSystem.slnx package --vulnerable --include-transitive --no-restore` | Cả bốn project không có package bị báo lỗ hổng theo nguồn NuGet hiện tại |
| `dotnet ef migrations list --project AutoDubbing.Data --startup-project AutoDubbing.API --no-build` | Kết nối SQL Server thành công bằng dependency sau bản vá; `InitialCreate` đã áp dụng, `ConfigureAdminAuthentication` còn `Pending`; chỉ đọc history |
| Thuộc tính MSBuild Data | `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, `NuGetAuditSuppress=[]`; `NoWarn` chỉ có mặc định `1701;1702`, không suppress NU1902/NU1903 |
| Các đoạn PowerShell trong tài liệu | 15 đoạn, 0 lỗi cú pháp khi parse bằng Windows PowerShell |

Trước khi người dùng áp dụng migration mới, đã đọc metadata trực tiếp từ SQL Server `.`, Windows Authentication, database `AutoDubbingDb`: khi đó chỉ có `20261019154054_InitialCreate`; cột username còn `nvarchar(max)` (`max_length = -1`), collation `SQL_Latin1_General_CP1_CI_AS`, chưa có unique index username. Server có collation `Latin1_General_100_BIN2`; truy vấn so sánh `Admin` với `admin` theo collation này cho kết quả khác nhau. Kết quả sau khi người dùng áp dụng migration được cập nhật bên dưới.

Đã đối chiếu `AppDbContext`, snapshot và migration mới: đều đặt `nvarchar(100)`, collation trên cột `Latin1_General_100_BIN2` và unique index đúng riêng cột username. Không cần sửa migration. `docs/verify-admin-auth.sql` dùng metadata để xác nhận lại sau khi bạn áp dụng migration, không chèn dữ liệu thử.

Ở bước xác minh này chỉ thay `AutoDubbing.Data.csproj`, cập nhật tài liệu này và thêm `docs/verify-admin-auth.sql`. Không tự tạo tài khoản, khóa/mở khóa tài khoản hoặc áp dụng schema thay người dùng.

## Tiến độ sau thiết lập local — 20/10/2026

Người dùng báo đã lưu key, áp dụng migration, nhận schema `PASS` và tự tạo Admin thành công. Đã chạy lại `docs/verify-admin-auth.sql` trực tiếp trên SQL Server, kết quả thật:

| Hạng mục | Kết quả |
| --- | --- |
| Kiểu và giới hạn username | `nvarchar(100)`, `max_length_bytes = 200`, NOT NULL |
| Collation trên cột username | `Latin1_General_100_BIN2` |
| Unique index username | `is_unique = 1`, `is_disabled = 0`, `has_filter = 0` |
| Ràng buộc trạng thái | Đang bật và đã được xác thực |
| Script xác minh tổng thể | **PASS** |
| API khởi động với cấu hình JWT local | Thành công, môi trường Development, HTTPS `7239` và HTTP `5133` |
| Request HTTPS thiếu token/token giả | Chưa có mã HTTP: máy chưa tin cậy chứng chỉ TLS phát triển |
| Login đúng, `/me` có token, sai mật khẩu, tài khoản Locked | Chưa có mã HTTP thực tế được cung cấp; chưa kết luận đạt |

API ban đầu chưa kết nối được tại cổng `7239`; đã khởi động bằng `dotnet run --project AutoDubbing.API --no-build --launch-profile https`. Kestrel báo chứng chỉ phát triển chưa được tin cậy, và request HTTPS xác nhận lỗi trust SSL/TLS. Không bỏ qua kiểm tra chứng chỉ.

Người dùng cần chạy `dotnet dev-certs https --trust` trong terminal của mình và chấp nhận hộp thoại tin cậy, sau đó làm bước 5–6 và báo các mã HTTP. Không cần gửi key, mật khẩu hoặc token. Các request HTTPS lỗi TLS chưa đi tới bước đánh giá xác thực, nên không được ghi nhận là kết quả `401`.

Theo yêu cầu kiểm tra hộ, đã thêm `scripts/verify-admin-login.ps1` để tự động chạy các ca với mật khẩu nhập cục bộ. Đã kiểm tra cú pháp bằng Windows PowerShell, 0 lỗi. Yêu cầu mở cửa sổ tương tác đã bị từ chối nên công cụ chưa được chạy; chưa có báo cáo HTTP thật và chưa đổi trạng thái tài khoản. Người dùng có thể chạy lệnh ở đầu bước 6 để nhập mật khẩu trên máy và tạo báo cáo; không cần chép kết quả chứa secret vào cuộc trò chuyện.
