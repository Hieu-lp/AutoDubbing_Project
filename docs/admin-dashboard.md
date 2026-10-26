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
này được chọn bằng `VITE_USE_MOCK_DATA=true` khi khởi động/build Vite, không
tự giới hạn ở development. Khi kết nối API thật hoặc build bàn giao, giữ
`VITE_USE_MOCK_DATA=false` (mặc định nếu không khai báo). Mục Bản dịch & audio
không hoạt động với đăng nhập/POI mô phỏng, tránh dùng nhầm ID demo trên API thật.

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

## Bản dịch & audio từ SQL

Đăng nhập Admin, mở **Bản dịch & audio**, chọn POI đã xuất bản và ngôn ngữ.
Danh mục ngôn ngữ đọc từ `/api/languages`; nội dung/audio đọc từ
`GET /api/pois/{id}/content?language=...`. Văn bản chỉ đọc; audio phát từ URL
HTTPS hợp lệ đã lưu trong SQL. Fallback được ghi rõ ngôn ngữ gốc, có nút
chuyển về bản gốc để nghe; không coi fallback là bản dịch đã hoàn tất.

Chỉ bấm **Chuẩn bị nội dung & audio** hoặc **Tạo audio còn thiếu** mới gửi
`POST /api/pois/{id}/prepare-content` cho đúng cặp đang chọn. Trang dùng chung
PreparationClient, queue và pipeline của khách: văn bản Ready được dùng lại,
thiếu audio thì tiếp tục TTS/cache/upload; không dịch lại không cần thiết.
Queued/Processing được polling bằng GET, hiển thị trạng thái/mã job/mã lỗi
và thời điểm thử lại nếu API cung cấp. Không tự retry POST khi mở trang, đổi
ngôn ngữ, gặp lỗi hoặc bị quota. Rời lựa chọn dừng polling trên frontend;
không hủy công việc đã được backend nhận. Lịch sử queue mất khi API restart,
nhưng kết quả SQL đã lưu vẫn đọc được sau F5; nút **Đọc lại dữ liệu** chỉ GET.

API quản trị `GET /api/admin/pois/{id}/content?language=...` và `/contents`
yêu cầu Admin, đọc được cả POI Hidden. `/contents` trả danh sách/trạng thái,
thời điểm bản dịch, thời lượng audio nếu có. `POST /api/admin/pois/{id}/prepare-content`
dùng cùng queue/service; chỉ chuẩn bị POI Published. API công khai không trả
POI Hidden. Chưa có nghiệp vụ duyệt/sửa
thủ công: đã bỏ nút tạo demo, lưu chờ duyệt, duyệt và giọng đọc trình duyệt.
Không đọc/xóa các bản nháp localStorage cũ và không tự nhập chúng vào SQL.
Không sửa văn bản hoặc thu hồi/ghi đè audio cũ trong thay đổi này.

## Tài khoản, lộ trình, sự kiện và nhật ký

- **Tài khoản**: `GET/POST /api/admin/users`, `PUT /api/admin/users/{id}`.
  Quản lý họ tên, tên đăng nhập, mật khẩu mới tùy chọn và Active/Locked; không
  trả hash, không xóa account/hồ sơ liên quan. Khóa tài khoản Active cuối
  cùng bị từ chối. Token của tài khoản Locked bị từ chối ở request kế tiếp.
- **Lộ trình gợi ý**: lưu SQL theo [walking-routes.md](walking-routes.md),
  giữ thứ tự admin; không còn menu hay nghiệp vụ Session tham quan.
- **Sự kiện sử dụng**: `GET /api/admin/usage-events`, lọc POI/ngôn ngữ/loại/
  sinceUtc; 24 giờ mặc định, tối đa 31 ngày và 200 dòng/request. Không CRUD
  sự kiện bằng tay. Khách POST `/api/usage-events` với eventId duy nhất,
  guestId, loại phát, POI/ngôn ngữ/audio thực tế hoặc route_selection.
  Backend kiểm tra dữ liệu và unique eventId; retry cùng event không tăng
  lượt. Frontend chỉ gửi sau `playing`, một lần/attempt, không theo render,
  polling hoặc buffering; Admin nghe thử không được tính là khách nghe.
- **Nhật ký hệ thống**: `GET /api/admin/logs`, lọc POI/ngôn ngữ/level/mã lỗi/
  sinceUtc. Worker ghi SQL sau tác vụ thành công/lỗi với POI, ngôn ngữ, job,
  bước và mã lỗi an toàn khi có. Log cũ không có metadata chỉ có mô tả chung;
  không trả thông điệp thô, exception hoặc stack trace. Lỗi ghi log không
  làm worker dừng xử lý; log tiến trình chỉ ghi loại exception đã lọc.

Dashboard đọc lượt nghe/lộ trình từ SQL 24 giờ. Số 0 là kết quả truy vấn đã
thành công nhưng chưa có sự kiện; lỗi đọc SQL là không có số liệu, không tự
thay bằng 0. Khách hoạt động là số mã khách ẩn danh có heartbeat trong 60
giây, nằm trong RAM và mất khi restart API; không phải số người xác minh.
Mã khách lưu sessionStorage riêng với token đăng nhập, không tạo lại bảng
Session. Các mục thống kê/log tự làm mới mỗi 10 giây và nêu rõ dữ liệu cũ/lỗi.

Kiểm thử giao diện nằm trong `fe-user-web/e2e/visitor.spec.mjs` (các ca
`admin content`), dùng API/audio fixture; backend đã có test tái sử dụng bản
dịch sau lỗi TTS trong `ContentGenerationTests`. Các ca này không chứng minh
chất lượng bản dịch/giọng đọc hay khả năng kết nối dịch vụ thật.
Playwright khởi động Vite ở `127.0.0.1:5174`; frontend dùng tay vẫn ở cổng 5173.

## Xem tác vụ lỗi và lý do pipeline

Trong Dashboard giám sát, bấm ô **Tác vụ lỗi → Xem chi tiết**. Danh sách cho
biết POI, ngôn ngữ, mã job, thời điểm kết thúc lỗi, bước đã ghi nhận, mã lỗi đã
lọc và giải thích. Dữ liệu còn thiếu hiển thị “Chưa ghi nhận”; không suy đoán
bước từ tên lỗi. Không có nút tự retry hoặc gọi provider khi xem dashboard.

Số lỗi và danh sách đọc từ cùng snapshot queue qua
`GET /api/admin/dashboard/summary?range=24h`, yêu cầu Admin đang hoạt động.
Bộ đếm gồm các lần xử lý đã kết thúc ở Failed, Unavailable hoặc QuotaExceeded,
kể cả tác vụ bị chặn trước khi chạy (khi đó chưa có bước thất bại). Lịch sử
này nằm trong RAM của một API, mất khi khởi động lại. Mặc định giữ tối đa 256
tác vụ hoàn tất trong 30 phút gần nhất; số thực tế phụ thuộc cấu hình và có
thể ít hơn khi đạt giới hạn. Không phải tổng lỗi trong 24 giờ. Đọc snapshot
cũng loại lịch sử hết hạn, không phải đợi tác vụ mới mới cập nhật bộ đếm.

Pipeline hiển thị riêng trạng thái hiện tại: cấu hình thiếu/không hợp lệ,
người vận hành tạm dừng, hoặc đang bị queue chặn với mã lỗi và mốc chờ thực.
Quota không có thời điểm phục hồi được ghi là cần người vận hành kiểm tra;
không dùng ngày vô hạn làm thời điểm retry. Hết thời gian chờ không bảo đảm
provider đã phục hồi. Lỗi cũ không làm pipeline bị đánh dấu đang ngừng nhận
tác vụ. Nếu queue còn ghi nhận một lượt Ready sau đó cho cùng POI/ngôn ngữ/
phiên bản nguồn, lịch sử lỗi hiển thị thời điểm thành công; vẫn giữ lần lỗi cũ.

**Nhật ký lỗi hệ thống** là nguồn SQL riêng trong 24 giờ, tối đa 5 bản ghi,
không phải lịch sử queue. Worker không tự lưu mọi lỗi job vào bảng này. Giao
diện phân biệt chưa có bản ghi với chưa đọc được SQL. Message/component thô
của SQL và stack trace không được chuyển tới frontend; mã lỗi/stage của job
chỉ đi qua danh sách đã phân loại an toàn.

Frontend mới vẫn mở được với image API cũ, nhưng sẽ báo chưa có chi tiết/lý
do nếu API chưa hỗ trợ các trường mới. Cần build và recreate API trong một
lượt triển khai riêng để dùng đủ chức năng. Khởi động lại không khôi phục được
job cũ đã mất trong RAM; không thể bổ sung ngược bước lỗi chưa từng được ghi.
