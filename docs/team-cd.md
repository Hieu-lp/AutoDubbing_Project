# CD cho từng máy Docker của thành viên

CI build/test rồi xuất bản một cặp image API/web lên GHCR. Mỗi máy nhận bản mới
độc lập bằng bộ cập nhật cục bộ; không cần runner GitHub Actions trên máy cá nhân.
Cloudflare chỉ phục vụ truy cập từ Internet, không tham gia phân phối bản cập nhật.

## Phần dùng chung trong repository

[Workflow](../.github/workflows/ci.yml) chỉ công bố khi push nhánh `develop` và
`build-and-test` đạt. Các nhánh khác vẫn chạy CI theo trigger hiện có.

1. Build API và web với tag `sha-<commit>` và label revision.
2. Sau khi cả hai image thành công, tạo manifest chứa digest chính xác của cả hai,
   commit, sequence và các migration chính thức của source. Publisher xác minh
   revision bằng Git HEAD đã checkout, dùng chung validator với updater.
3. Cập nhật tag `develop` cho Compose cũ.
4. Xuất bản image nhỏ `ghcr.io/<owner>/autodubbing-release:develop` **sau cùng**.
   Đây là tín hiệu phát hành mà updater theo dõi. Nếu build/publish dở dang,
   các máy vẫn dùng bản cũ. Updater không tự ghép hai tag `develop` có thể khác bản.

Manifest chỉ chứa metadata; không chứa env, key hay dữ liệu SQL. Updater đọc file
trong image manifest bằng `docker create/cp/rm`, không khởi chạy image này.

Contract `release.json` format 1: `format=1`, `channel=develop`, `revision` là Git
SHA đầy đủ 40 ký tự, `sequence = github.run_number * 1000 + github.run_attempt`
(attempt từ 1 đến 999), `images` có đúng `api`/`user-web` ghim GHCR bằng digest,
`migrations` là danh sách MigrationId chính thức, sắp xếp, không trùng. Sequence
mới cho lần rerun tránh tái dùng sequence với digest build khác; run tiếp theo
luôn lớn hơn mọi attempt hợp lệ của run trước. Khi develop đã tiến lên, job từ
commit cũ dừng trước khi đổi channel.

Digest lấy trực tiếp từ output `digest` của từng build/push action: đó là digest
đối tượng registry được công bố, có thể là OCI index hoặc platform manifest.
Nó không mặc định là image config ID. Updater resolve hai phía về cùng platform
và so RootFS/config của image để xác minh container, không so hai loại ID khác
nhau. Fingerprint nội dung platform là giá trị nội bộ, không gọi là OCI digest.

Thay đổi local chưa commit/push sẽ chưa xuất hiện trong GitHub Actions. Sau khi
người quản lý đưa thay đổi vào `develop`, kiểm tra job công bố manifest đạt trước
khi yêu cầu các thành viên kiểm tra CD. Không coi kiểm thử local là đã chạy CD qua GHCR.

## Thiết lập một lần trên mỗi máy Windows + WSL

Yêu cầu: bộ API/web/SQL hiện có đang chạy đúng dữ liệu, Docker Compose hỗ trợ
`env_file.format: raw`, Python 3.10+ trong WSL, và source chứa thư mục
[scripts/cd](../scripts/cd). Dùng **đúng distro, project và env file của máy đó**.
Bộ cài nhận diện Compose từ container hiện có; không tạo môi trường/database mới.

Nếu image GHCR riêng tư, đăng nhập **trong đúng WSL/user đang chạy Docker**:

```powershell
wsl -d Ubuntu-E --exec docker login ghcr.io
```

Nhập username và PAT classic có `read:packages` tại prompt đăng nhập. Token cần
được cấp quyền đọc **cả ba** package API, web và release, cùng quyền SSO nếu tổ
chức yêu cầu. Không đưa token vào dòng lệnh, file env, source hoặc tin nhắn.
Package public có thể đọc ẩn danh. Xem [GHCR authentication](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).

Ví dụ cho máy đang dùng project `autodubbing-team` tại thư mục source:

```powershell
.\scripts\cd\Install-TeamUpdater.ps1 -Distro Ubuntu-E -Project autodubbing-team
```

**Máy handover hiện tại** dùng project/đường dẫn khác:

```powershell
.\scripts\cd\Install-TeamUpdater.ps1 -Distro Ubuntu-E -Project autodubbing-handover `
  -EnvFile E:\AutoDubbing-Handover\.env.team
```

Nếu source thuộc owner GitHub khác, thêm `-RegistryPrefix ghcr.io/<owner>` khi
khởi tạo. Cấu hình được lưu ở `artifacts/cd/host.json`, riêng trên từng máy và
được Git ignore. Bộ cài không sửa hai env hiện có. Nó đối chiếu env đã resolve
với API đang chạy, giữ `AcceptNewGeneration=true`, rồi kiểm tra dữ liệu thật
trước khi đăng ký lịch. Khi không khớp, nó dừng và không cài lịch.

Scheduled Task tên `AutoDubbing-CD-<project>` kiểm tra mỗi 5 phút khi Windows user
đó đang đăng nhập. Nó chạy ẩn, không tương tác, giới hạn 30 phút cho mỗi lần gọi
WSL và không chạy chồng. Docker phải sẵn sàng; nếu SQL đang dừng, updater báo
blocked và không khởi tạo SQL. Máy offline giữ bản đang có và thử lại ở lịch sau.
Windows dùng launcher dạng WindowsApplication với `CreateNoWindow`, thay vì gọi
PowerShell trực tiếp từ task. Bộ cài biên dịch launcher bằng Windows PowerShell
5.1 có sẵn; binary và log nằm cạnh cấu hình CD, không đưa vào Git. Nếu đã cài bản
cũ bị bật cửa sổ, sửa riêng action mà không đụng Docker/database hoặc đổi lịch:

```powershell
.\scripts\cd\Install-TeamUpdater.ps1 -Distro Ubuntu-E -Project autodubbing-handover -RepairLauncher
```

Lỗi launcher/PowerShell được lưu trong `artifacts/cd/launcher.log` (xoay ở 1 MiB).
Chỉ một cấu hình CD mặc định trong mỗi bản checkout; dùng `-ConfigPath` riêng
nếu chủ động quản lý nhiều project trong cùng checkout.

## Kiểm tra và mở lại

```powershell
# Đọc trạng thái, không triển khai
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command status

# Kiểm chứng database/API/web đang chạy, không tạo nội dung
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command verify

# Đọc descriptor, tải cặp image để kiểm tra revision/source; không triển khai
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command release

# Nhận bản phát hành mới ngay
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command check

# Sau khi Docker hoạt động, mở lại đúng SQL cũ và bản API/web CD đã lưu
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command start
```

Sau khi dùng CD, dùng lệnh `start` ở trên để giữ đúng bản đã chọn. Các lệnh mở
local cũ có thể cố định image trước CD. Không dùng Compose của project khác để mở
lại hệ thống này. URL mặc định: web `http://localhost:18081/`, Admin
`http://localhost:18081/admin`, API `http://localhost:18080/api/pois`.

## Máy Linux hoặc Docker Desktop chạy Python trực tiếp

Chạy cùng updater từ môi trường Docker client nhìn thấy các đường dẫn Compose:

```bash
python3 scripts/cd/updater.py init --project autodubbing-team
python3 scripts/cd/updater.py verify
python3 scripts/cd/updater.py check
```

Linux: dùng `python3 scripts/cd/install_timer.py` để đăng ký systemd user timer
5 phút; user service cần user manager đang chạy. Dùng `--remove` để gỡ timer.
Docker Desktop Windows: bỏ `-Distro` trong các lệnh PowerShell và cài Python 3.10+
trong Windows. Docker credentials phải thuộc đúng user/môi trường thực thi.

## Bảo toàn dữ liệu và kiểm chứng triển khai

Updater chỉ gọi Compose `up` cho `api user-web` với `--no-deps --no-build --pull never`.
Nó giữ ID container SQL, tên volume và database đã nhận diện lúc cài; không có
lệnh down/xóa volume/import seed/create-admin/reset tài khoản hoặc tự migration.
Mỗi máy giữ dữ liệu và secrets riêng; CD không chuyển tài khoản từ máy đồng đội.

Trước triển khai, updater kiểm tra migration **khớp chính xác** manifest. Nếu
khác, bản đó bị chặn: người quản lý cần sao lưu database máy đó bằng backup SQL
đầy đủ/COPY_ONLY WITH CHECKSUM, kiểm tra backup có thể phục hồi, rồi áp dụng
migration chính thức tương ứng trên database đích trước khi chạy lại `check`.
Không tự rebuild log hoặc quay lùi schema để vượt chặn. Bản hiện tại đã đủ
migration thì không có bước sửa SQL nào chạy lại.

Kiểm tra sau cập nhật gồm danh sách và chi tiết POI thật, đối chiếu ID/tên với
SQL được bảo vệ, nội dung/audio Ready có sẵn khi có, web/Admin HTML và kết nối
SQL từ chính API container. Danh sách POI phải có dữ liệu; không ép theo seed
8/19/26. Không gửi POST tạo nội dung, gọi Gemini hoặc reset/login Admin tự động.
Kiểm tra audio ở đây là metadata Ready trong API; phát audio qua trình duyệt và
đăng nhập Admin là kiểm tra thủ công bằng dữ liệu/thông tin đăng nhập hiện có.

Khi bản mới lỗi, updater khôi phục **cả hai image cũ theo image ID** và kiểm tra
lại dữ liệu. Journal được ghi trước khi recreate; lần chạy sau khôi phục nếu lần
trước bị ngắt. Bản lỗi không tự triển khai lặp mỗi 5 phút; dùng `-RetryFailed`
với `check` sau khi đã xử lý nguyên nhân, hoặc đợi bản mới. Nếu rollback cũng
không đạt, journal được giữ lại và updater báo cần xử lý. Nó không sửa database.

Log an toàn trong `artifacts/cd/events.jsonl` (xoay ở 5 MiB), trạng thái mới nhất
trong `status.json`, image/journal trong `state.json`. Không in Docker inspect,
Compose resolved, connection string, key, mật khẩu hoặc hash tài khoản vào log.

Khởi động lạnh WSL: updater chờ Docker tối đa 180 giây và database hiện có tối
đa 600 giây; không tự init/repair/migrate. SQL container cần restart policy
`unless-stopped` như Compose và Docker system service enabled. Policy được sửa
trên container hiện có bằng bước vận hành riêng, không recreate SQL khi deploy.
WSL phải đang chạy để phục vụ web; dịch vụ systemd không tự giữ WSL sống khi
đóng hết phiên. Khi dùng máy làm server, giữ phiên Ubuntu-E đang mở hoặc một
tiến trình host WSL đang được quản lý sẵn. Không tắt toàn bộ WSL để kiểm thử CD
khi đang phục vụ dữ liệu/tunnel.

Danh sách file để review/công bố và quy trình kiểm chứng tự động đầu-cuối nằm
trong [checklist công bố](cd-publish-checklist.md).

## Thử rollback cục bộ và gỡ lịch

```powershell
# Recreate cùng image hiện có, kiểm tra dữ liệu rồi cố tình báo lỗi để thử rollback.
# API/web có gián đoạn ngắn; SQL/volume/database được giữ nguyên.
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command exercise

# Gỡ riêng lịch tự cập nhật, giữ nguyên hệ thống Docker
.\scripts\cd\Install-TeamUpdater.ps1 -Project autodubbing-handover -Remove
```

Kiểm thử tự động, **Ubuntu-E tại source**:

```bash
cd /mnt/e/AutoDubbingSystem
python3 -m unittest discover -s scripts/cd/tests -v
```
Để xác nhận CD đầu-cuối: một bản mới lên `develop`, job CI/publish đạt, từng máy
báo `deployed` với cùng revision/sequence và API đọc đúng dữ liệu của máy đó.
Mở trang HTTP 200 hoặc chạy exercise local chưa chứng minh đường GHCR hoạt động.

## Cloudflare để chia sẻ máy cá nhân (tùy chọn)

Ví dụ đúng network handover hiện tại:

```powershell
wsl -d Ubuntu-E --exec docker run --rm --name autodubbing-cloudflared `
  --network autodubbing-handover_default cloudflare/cloudflared:latest `
  tunnel --no-autoupdate --url http://user-web:80
```

Mỗi máy mở tunnel riêng và nhận URL riêng. Quick Tunnel dừng khi tiến trình dừng;
dùng tunnel có hostname cố định nếu cần URL lâu dài. Xem
[Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/).
