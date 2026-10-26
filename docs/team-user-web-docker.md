# Frontend user web trong Docker — bước tích hợp local

## Cập nhật tích hợp 7 thực thể (27/10/2026)

Source mới có lộ trình gợi ý, quản lý tài khoản, sự kiện nghe và nhật ký SQL;
schema mới bỏ Session tham quan. Xem [thiết kế/ERD hiện hành](backend-demo.md),
[quản trị](admin-dashboard.md) và [lộ trình](walking-routes.md). Không suy ra
container đang chạy đã cập nhật chỉ vì npm build hoặc test đạt.

Trước migration demo: xác minh container/database local, số bản ghi và FK;
backup SQL COPY_ONLY WITH CHECKSUM ra ngoài repo, RESTORE VERIFYONLY và thử
restore/upgrade trên DB riêng. Migration giữ lộ trình, không xóa dữ liệu
POI/bản dịch/audio/tài khoản/sự kiện/log. Không chạy Down để khôi phục Session;
phải dùng backup. `schema.sql` là script idempotent đã bao gồm migration mới.

Sau khi source/DB đã được kiểm tra, **bước triển khai kế tiếp** (không tự chạy
trong lượt tích hợp) là build cả API/frontend, rồi recreate đúng hai dịch vụ
`api user-web` bằng **Compose và project đang dùng**, giữ nguyên SQL/volume/
env/key. Không `down -v`, không tạo stack khác hoặc import lại demo. Ví dụ
với bộ hiện tại trên Windows:

```powershell
Set-Location 'D:\New folder\AutoDubbing_Project'
docker build -t autodubbing-api:dev -f Dockerfile .
docker build -t autodubbing-user-web:dev -f fe-user-web/Dockerfile.team .
Set-Location 'D:\AutoDubbing-Handover'
docker compose --env-file .env.team -f compose.handover.yaml up -d --no-deps --force-recreate api user-web
```

Không xuất image/checksum mới nếu chưa cần bàn giao snapshot. Dockerfile
frontend ép `VITE_USE_MOCK_DATA=false`; thay biến runtime sau build không đổi
bundle Vite. API/SQL và auth key tiếp tục nằm phía server.

### Demo bốn thiết bị trong cùng mạng

Trình duyệt chỉ gọi `/api` cùng origin; không dùng localhost của điện thoại
để gọi máy chủ. Nginx chuyển tiếp API trong mạng Docker. Source Compose có
`WEB_BIND_ADDRESS` mặc định 127.0.0.1; để LAN truy cập, đặt 0.0.0.0 khi triển
khai và đồng bộ riêng dòng port user-web vào Compose bàn giao nếu nó còn
hardcode loopback. Giữ SQL/API bind loopback. Dùng IP LAN của máy chủ ở cổng
18081, không phải localhost của thiết bị. Kiểm tra firewall chỉ cho cổng web
trên mạng riêng theo chính sách máy; không mở SQL ra LAN.

GPS trình duyệt cần secure context và quyền vị trí: dùng cùng một HTTPS
origin/reverse proxy/tunnel đã được cấu hình khi test GPS trên điện thoại.
HTTP qua IP LAN có thể đọc/chọn điểm/phát thủ công nhưng không chứng minh GPS
thật. Chọn điểm bản đồ luôn có nhãn vị trí đã chọn, không giả làm GPS.

Mỗi phiên khách có mã ẩn danh riêng trong sessionStorage và heartbeat 30 giây
khi trang đang hiển thị. Dashboard đếm mã hoạt động 60 giây, không đếm request;
RAM này mất khi restart API. Thử nhiều context trình duyệt chỉ xác minh logic,
không phải kiểm chứng bốn thiết bị vật lý, mạng LAN, HTTPS hoặc GPS thực địa.

Demo lần lượt: admin sửa POI → user làm mới/đợi 10 giây; admin xuất bản gợi ý
→ user chọn vị trí xuất phát rồi chọn gợi ý → hiện thứ tự/đường; admin sửa
thứ tự → user nhận thay đổi và tính lại; admin ẩn/xóa → gợi ý/đường cũ bị gỡ.
User phát audio → Sự kiện sử dụng/Dashboard tăng từ SQL; job lỗi → Nhật ký hệ
thống cho biết POI/ngôn ngữ/job/bước nếu đã ghi nhận. API lỗi phải hiện lỗi
hoặc dữ liệu cũ, không hiển thị danh sách mẫu. Không tự chạy batch/provider
thật trong kiểm thử; chỉ thử cặp cụ thể sau khi được cho phép.

Phạm vi: thêm frontend React/Vite đã có vào Compose SQL Server + API hiện tại. Chưa phải bộ bàn giao hoàn chỉnh cho máy mới.

## Các file trong gói

Giải nén nội dung gói vào `E:\AutoDubbingSystem`:

- `compose.team.yaml`: thay bản cũ; giữ nguyên hai dịch vụ sqlserver/api, volume, JWT và generation=false của Docker.
- `fe-user-web/Dockerfile.team`: Node 22 build bằng npm ci và npm run build; Nginx phục vụ dist.
- `fe-user-web/Dockerfile.team.dockerignore`: giới hạn build context vào frontend và bỏ qua môi trường riêng/node_modules/dist.
- `fe-user-web/nginx.team.conf`: chuyển /api/* tới http://api:8080, giữ đường dẫn và query string; hỗ trợ reload đường dẫn SPA.
- Tài liệu này.

Không sửa package.json, vite.config.ts, .env local, giao diện hoặc backend. File cấu hình riêng .env.team và .env.api.team đã tạo vẫn ở thư mục gốc trên máy bạn. Không có mật khẩu/key trong gói này.

## Build từ Ubuntu-E

Mở PowerShell nếu chưa có terminal Ubuntu-E:

```powershell
wsl -d Ubuntu-E -u dell
```

Trong Ubuntu-E, tạo bản sao chỉ gồm frontend trên filesystem Linux để tránh build context chậm qua /mnt/e. Đây là bản sao để build; chỉnh code tại E:\AutoDubbingSystem như trước.

```bash
cd /mnt/e/AutoDubbingSystem
taskWebBuildDir=$(mktemp -d "$HOME/autodubbing-web-build.XXXXXX")
mkdir -p "$taskWebBuildDir/fe-user-web"
set -o pipefail
tar -C /mnt/e/AutoDubbingSystem/fe-user-web \
  --exclude='node_modules' --exclude='dist' \
  --exclude='.env*' --exclude='.git' --exclude='artifacts' \
  --exclude='playwright-report' --exclude='test-results' \
  -cf - . | tar -C "$taskWebBuildDir/fe-user-web" -xf -
```

Nếu sao chép không có lỗi, build:

```bash
docker build --progress=plain \
  -f "$taskWebBuildDir/fe-user-web/Dockerfile.team" \
  -t autodubbing-user-web:dev \
  "$taskWebBuildDir"
```

Cần có package-lock.json hiện tại của dự án để npm ci sử dụng đúng dependency. Nếu npm ci/build thất bại, giữ log và sửa lỗi thực tế, không tự xóa lock file hoặc nâng package để bỏ qua lỗi.

## Khởi động

Vẫn trong Ubuntu-E:

```bash
cd /mnt/e/AutoDubbingSystem
docker compose --env-file .env.team -f compose.team.yaml config --quiet
```

Nếu cấu hình không lỗi:

```bash
docker compose --env-file .env.team -f compose.team.yaml up -d --no-build user-web
docker compose --env-file .env.team -f compose.team.yaml ps
```

Không cần chạy lại migration hoặc script nhập dữ liệu. Volume SQL đang có dữ liệu được giữ nguyên. Không dùng down -v.

## URL và xác minh

- Web: http://localhost:18081
- API thông qua proxy web: http://localhost:18081/api/pois
- Settings thông qua proxy: http://localhost:18081/api/content-preparations/settings
- API trực tiếp: http://localhost:18080/api/pois
- SQL từ SSMS Windows: localhost,11433

Kỳ vọng danh sách có 8 POI và settings có 500 m / 50 m / 3 giây. Dùng một POI đã có audio như Nhà thờ Đức Bà để kiểm tra phát thủ công, rồi chọn vị trí dưới 50 m và ở yên đủ 3 giây để kiểm tra thuyết minh. Nếu trình duyệt chặn autoplay, dùng nút Phát. Kiểm tra thêm ra ngoài trước 3 giây để hủy đếm.

Generation của Docker vẫn false. Chọn nội dung thiếu có thể trả GenerationPaused; audio Ready vẫn được phục vụ. ORS/Gemini/Supabase upload chưa được cấu hình trong môi trường Docker này. Không coi việc web chạy là đã kiểm chứng tạo mới hoặc lộ trình ORS.

Vite API_PROXY_TARGET chỉ dùng khi chạy dev/preview. Bản build Docker dùng VITE_API_BASE_URL=/api tại lúc build và Nginx chuyển yêu cầu tới tên dịch vụ api. Trình duyệt không trực tiếp gọi hostname api của Docker.

## Khi gặp lỗi

```bash
docker compose --env-file .env.team -f compose.team.yaml logs --tail 80 user-web
docker compose --env-file .env.team -f compose.team.yaml logs --tail 80 api
```

Nếu trang web mở nhưng /api/pois lỗi, xem log cả user-web và api. Nginx kiểm tra sức khỏe /healthz chỉ xác nhận Nginx chạy, không xác nhận API hay nội dung giao diện.

## Sau khi chỉnh frontend

Bản này phục vụ build tĩnh, không tự cập nhật khi sửa mã. Tạo snapshot mới và build lại theo các lệnh trên, sau đó:

```bash
cd /mnt/e/AutoDubbingSystem
docker compose --env-file .env.team -f compose.team.yaml up -d --no-build user-web
```

Các lần npm run dev trên Windows hiện có vẫn dùng cấu hình Vite riêng như trước. Cổng 18081 được chọn để tránh chiếm cổng 5173.

## Giới hạn xác minh của gói

Đã đối chiếu các file cấu hình người dùng cung cấp, kiểm tra YAML và xác nhận các định nghĩa SQL/API/volume trước đó không thay đổi. Chưa chạy npm build, Docker build, Nginx hoặc trình duyệt với source frontend hiện tại vì source đầy đủ nằm trên máy người dùng. Cần thực hiện các bước trên để xác minh thực tế.
