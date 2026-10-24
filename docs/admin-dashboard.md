# Dashboard quản trị POI

Frontend quản trị nằm trong `fe-user-web`, tách biệt với trang khách tại `/`.
Dashboard `/admin` có đăng nhập Admin và hỗ trợ tìm kiếm, lọc trạng thái, tạo,
cập nhật, xuất bản/ẩn và xóa POI.

## Chạy chế độ demo không cần database

Chế độ phát triển của frontend dùng tài khoản và POI mẫu trong
`fe-user-web/src/adminMockData.json`; không cần chạy API, SQL Server hoặc tạo
tài khoản thật. Chạy:

```powershell
Set-Location fe-user-web
npm ci
npm run dev
```

Mở `http://localhost:5173/admin` và đăng nhập bằng tài khoản demo:

- Tên đăng nhập: `admin`
- Mật khẩu: `admin123`

Thêm, sửa và xóa POI được lưu trong local storage của trình duyệt. Xóa key
`voicemap.admin.demo.pois` trong local storage để khôi phục dữ liệu mẫu. Chế độ
này chỉ bật khi chạy Vite development; bản production tiếp tục gọi API thật và
không chấp nhận tài khoản demo.

## Chạy kết nối API thật

1. Đảm bảo SQL Server, database và tài khoản Admin đã sẵn sàng theo hướng dẫn
   [admin-auth.md](./admin-auth.md).
2. Mở terminal thứ nhất tại workspace:

   ```powershell
   dotnet run --project AutoDubbing.API --launch-profile https
   ```

3. Mở terminal thứ hai:

   ```powershell
   Set-Location fe-user-web
   npm ci
   npm run dev
   ```

4. Mở `http://localhost:5173/admin` và đăng nhập bằng tài khoản Admin đã tạo.

Vite chuyển tiếp `/api` tới API HTTPS ở `https://localhost:7239`. Nếu cổng
backend thay đổi, cập nhật `server.proxy['/api'].target` trong
`fe-user-web/vite.config.ts`.

## API quản lý

Các endpoint `GET`, `POST`, `PUT` và `DELETE` tại `/api/admin/pois` yêu cầu
Bearer token lấy từ `POST /api/admin/auth/login`. Danh sách Admin gồm cả POI
đang xuất bản và POI đang ẩn; API công khai tiếp tục chỉ trả POI đã xuất bản.

Không xóa POI đang có bản dịch, audio hoặc lịch sử sử dụng; API trả `409
Conflict` để giữ nguyên dữ liệu liên quan. Ví dụ request đầy đủ nằm trong
[AdminPois.http](../AutoDubbing.API/AdminPois.http).
