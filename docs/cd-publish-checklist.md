# Công bố publisher và kiểm chứng CD trên từng máy

Nhánh chuẩn bị hiện tại: `chore/cd-handover`, base `origin/develop`. Quy trình
repository dùng branch → Pull Request vào `develop` → CI đạt → người quản lý
merge. Publisher chỉ chạy khi push vào `develop`; CI của PR không publish.
Các lệnh commit/push dưới đây dành cho người quản lý chạy sau khi review, chưa
được agent thực thi trong lượt chuẩn bị này.

## File cần đưa lên develop

| File | Mục đích |
|---|---|
| [.dockerignore](../.dockerignore) | Giữ thay đổi loại credentials/database/backup/log khỏi build context đã có từ trước |
| [ci.yml](../.github/workflows/ci.yml) | Test, publish hai image có revision/source, xuất descriptor cuối cùng |
| [create_release.py](../scripts/cd/create_release.py) | Dùng chung schema validator, Git HEAD, sequence có run attempt |
| [updater.py](../scripts/cd/updater.py) | Updater hiện có: startup wait, pair/DB guard, rollback, kiểm tra identity cùng platform, đọc release không deploy |
| [Invoke-TeamUpdate.ps1](../scripts/cd/Invoke-TeamUpdate.ps1) | CLI PowerShell/WSL hiện có |
| [Install-TeamUpdater.ps1](../scripts/cd/Install-TeamUpdater.ps1) | Cài/repair Scheduled Task hiện có |
| [RunTeamUpdateHidden.cs](../scripts/cd/RunTeamUpdateHidden.cs) | Launcher Windows không tạo console |
| [install_timer.py](../scripts/cd/install_timer.py) | Bộ cài Linux timer đã có |
| [test_updater.py](../scripts/cd/tests/test_updater.py) | Kiểm tra guard/rollback/startup/identity và release read-only |
| [test_publisher.py](../scripts/cd/tests/test_publisher.py) | Kiểm tra contract publisher → updater, source SHA và sequence |
| [Test-HiddenLauncher.ps1](../scripts/cd/tests/Test-HiddenLauncher.ps1) | Kiểm tra launcher Windows đã có |
| [team-cd.md](team-cd.md) | Cài đặt/vận hành từng máy, contract release |
| [team-user-web-docker.md](team-user-web-docker.md) | Liên kết hướng dẫn CD, phân biệt mở snapshot local |
| [cd-publish-checklist.md](cd-publish-checklist.md) | Danh sách review và thử tự động đầu-cuối này |

Không đưa env, credential Docker/GitHub/UserSecrets, database, backup hoặc
thư mục artifacts/log vào commit. Binary launcher và host.json là trạng thái
riêng từng máy. Các máy đã cài không cần cài thêm một cơ chế CD khác.

## Kiểm tra và review trước khi công bố

**PowerShell, tại source:**

```powershell
Set-Location E:\AutoDubbingSystem
git status --short --branch
git diff --check
git diff -- .dockerignore .github/workflows/ci.yml docs/team-user-web-docker.md
python.exe -m unittest discover -s scripts/cd/tests -v
```

Đọc thêm các file mới trong bảng bằng Changes/VS Code; git diff thông thường
không hiển thị nội dung các file untracked. Workflow đã được kiểm tra actionlint
trong lượt chuẩn bị; kết quả GitHub CI thực tế vẫn cần chạy sau khi công bố.

**PowerShell — chỉ stage các file đã review:**

```powershell
Set-Location E:\AutoDubbingSystem
$cdFiles = @(
  '.dockerignore',
  '.github/workflows/ci.yml',
  'scripts/cd/create_release.py',
  'scripts/cd/updater.py',
  'scripts/cd/Invoke-TeamUpdate.ps1',
  'scripts/cd/Install-TeamUpdater.ps1',
  'scripts/cd/RunTeamUpdateHidden.cs',
  'scripts/cd/install_timer.py',
  'scripts/cd/tests/test_updater.py',
  'scripts/cd/tests/test_publisher.py',
  'scripts/cd/tests/Test-HiddenLauncher.ps1',
  'docs/team-cd.md',
  'docs/team-user-web-docker.md',
  'docs/cd-publish-checklist.md'
)
git add -- $cdFiles
git diff --cached --check
git diff --cached --stat
git diff --cached --name-only
$unexpectedFiles = @(git diff --cached --name-only | Where-Object { $_ -notin $cdFiles })
if ($unexpectedFiles.Count) { throw 'Index contains files outside the reviewed CD list; review before committing.' }
```

**PowerShell — người quản lý chạy sau khi chấp thuận diff:**

```powershell
Set-Location E:\AutoDubbingSystem
if ((git branch --show-current) -ne 'chore/cd-handover') { throw 'Verify the intended feature branch first.' }
git commit -m "Complete per-machine CD publishing and startup verification"
git push -u origin chore/cd-handover
```

Tạo Pull Request trên GitHub, base `develop`, compare `chore/cd-handover`. Chờ
build-and-test đạt; publish sẽ skipped trên PR. Sau review và merge, chờ job
publish trên push `develop` đạt. Không nhập lại seed hoặc chạy migration để thử.

## Contract và các điểm cần xem trong Actions

- Cả hai image có tag theo SHA và label revision/source từ cùng GitHub SHA/repo.
- Descriptor chỉ được dựng sau khi cả hai build/push đạt, dùng chính output digest
  của từng action; publisher đối chiếu revision với Git HEAD đã checkout.
- Format 1: channel develop, Git SHA 40 ký tự, sequence = run number × 1000 + run
  attempt (1..999), cặp api/user-web ghim digest, migrations chính thức sắp xếp.
- Alias develop tương thích Compose cũ được cập nhật; image release develop
  chứa release.json được công bố sau cùng. Lỗi một image/chưa đến bước cuối giữ
  channel release cũ. Rerun commit cũ không được quảng bá nếu develop đã tiến lên.
- OCI index, platform manifest và image config digest là các loại khác nhau.
  Digest trong descriptor là đối tượng registry được build/push action trả về.
  Đối chiếu runtime qua platform filesystem/config đã resolve, không so một
  registry index digest với một image config ID rồi kết luận khác phiên bản.

## Kiểm chứng tự động đầu-cuối trên máy handover

Trước khi merge/publish, lưu mốc hiện tại. Không gọi deploy thủ công trong phép
thử này. **PowerShell:**

```powershell
Set-Location E:\AutoDubbingSystem
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command status
Get-ScheduledTaskInfo -TaskName 'AutoDubbing-CD-autodubbing-handover'
wsl -d Ubuntu-E -u dell --exec docker inspect --format '{{.Image}}' autodubbing-handover-api-1 autodubbing-handover-user-web-1
```

Đảm bảo baseline SQL/API đọc được dữ liệu hiện có. **PowerShell:**

```powershell
Set-Location E:\AutoDubbingSystem
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command verify
```

Nếu WSL/SQL đang dừng, mở lại đúng bộ đã lưu trước. **PowerShell:**

```powershell
Set-Location E:\AutoDubbingSystem
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command start
```

Sau khi job publish đạt, có thể đọc descriptor và kiểm tra cặp image mà không
triển khai. **PowerShell:**

```powershell
Set-Location E:\AutoDubbingSystem
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command release
```

Lệnh release chỉ tải/đọc metadata, kiểm tra revision/source và không đổi state
phiên bản, container hoặc SQL. Nếu package release riêng tư, đăng nhập tại prompt
trong đúng WSL user, không dán token vào chat. Hai image API/web hiện tại đã tải
được công khai. **PowerShell, chỉ khi cần quyền đọc package private:**

```powershell
wsl -d Ubuntu-E -u dell --exec docker login ghcr.io
```

Giữ task đã cài được bật, chờ lượt lịch tự chạy sau khi có release mới. **PowerShell:**

```powershell
Enable-ScheduledTask -TaskName 'AutoDubbing-CD-autodubbing-handover'
Get-ScheduledTaskInfo -TaskName 'AutoDubbing-CD-autodubbing-handover'
Get-Content E:\AutoDubbingSystem\artifacts\cd\events.jsonl -Tail 30
Set-Location E:\AutoDubbingSystem
.\scripts\cd\Invoke-TeamUpdate.ps1 -Distro Ubuntu-E -Command status
```

Chỉ kết luận tự động đầu-cuối khi có đủ: Actions thành công cho commit mới;
descriptor/cặp image cùng revision; lượt lịch thực sự triển khai; event deployed
và sequence mới; runtime khớp cặp image đã resolve; API trực tiếp/proxy đọc đúng
SQL, Ready content/audio sẵn có; SQL container/volume/env và số lượng dữ liệu giữ
nguyên. Kiểm tra source revision/sequence, không yêu cầu digest build mới bằng
digest của build trước dù cùng commit.

Một lượt up_to_date không chứng minh đã chuyển phiên bản. Nếu chủ động gọi cập
nhật hoặc kích hoạt task, đánh dấu rõ đó là kiểm tra thủ công, chưa phải
lượt tự kích hoạt theo lịch. Không dùng exercise local làm bằng chứng nhận release
GHCR. Không tạo nội dung thiếu hoặc gọi Gemini để kiểm thử.
