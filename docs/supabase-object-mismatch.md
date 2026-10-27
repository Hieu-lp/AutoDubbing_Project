# SupabaseObjectMismatch — chẩn đoán và sửa ngày 26/10/2026

## Bằng chứng trên máy nhận bàn giao

Đã đọc `D:\Ban_giao\README-HANDOVER.md`. Bộ đang chạy là
`autodubbing-handover`, Compose tại `D:\AutoDubbing-Handover`, API cổng
18080, web Docker cổng 18081. Frontend Vite cổng 5173 là source riêng.

- Container API trước bản sửa: image `autodubbing-api:dev`, digest
  `sha256:9155eb1408337a865f2ee324c12076f762f1893cb8f33e9fcee3d4c4a7e9849e`.
  Đây là bản đã build lại từ source để thêm Admin, không phải image trong TAR.
- Image API trong `D:\Ban_giao\autodubbing-images.tar`: OCI digest
  `sha256:c323317b23baf384f4faa909574e1e682d3aeb3a3878e9f9a77add7a959352a9`.
- Generation đang bật. Cache được giữ trong volume
  `autodubbing-handover_api-audio-cache` tại `/app/artifacts/audio-cache`.
- Cache chỉ có file `generated/poi-2/vi/d82ef425ac0a08f2c9b8e201994c8c964125ced6e09d7b2be54480647905df84.wav`:
  1.584.044 bytes, SHA-256
  `68196db8e8f689f37ba878b2bf0e0fda2e0152e524a0a2bcf808bd48db95b948`.
- GET public cùng đường dẫn trong bucket `poi-audio` trả HTTP 200,
  MIME `audio/wav`, header RIFF, nhưng dài 1.595.564 bytes, SHA-256
  `b0e3cb9c1a0956466f13c70432421863626accf8bfb74642d07ccddeab2839fb`.
- SQL có tổng 26 audio; POI 2 chưa có bản dịch hoặc audio. Không có thao tác
  ghi SQL trong lượt chẩn đoán này.
- Log container sau lần recreate trước đó không còn bản ghi lỗi cũ trong 500
  dòng cuối đã lọc. Không suy diễn từ việc không tìm thấy log rằng lỗi đã hết.

## Nguyên nhân

Tên file cũ là `generated/poi-{id}/{language}/{sha256(text)}.wav`.
POI/ngôn ngữ đã được tách riêng, nhưng hai máy dùng cùng bucket, cùng POI,
ngôn ngữ và văn bản sẽ dùng chung object key. File cache và file remote khác
bytes là bằng chứng trực tiếp của xung đột này. Không xác định được máy hoặc
lượt nào đã tạo file remote; TTS có thể sinh bytes khác cho cùng transcript.
MIME của file đang gây lỗi đúng WAV, nên không phải nhánh MIME sai trong ca này.

SupabaseAudioStorage kiểm tra file public trước upload và yêu cầu bytes trùng
hoàn toàn. Điều này đúng với retry cùng một rendition, nhưng không đúng khi
object key chỉ nhận diện transcript. Worker giữ lỗi storage ở cấp queue, nên
yêu cầu POI 2 / zh-hans có thể nhận lỗi từ job vi trước đó (`jobId=null`) mà
không gọi dịch/TTS tiếng Trung. Đây không phải bằng chứng TTS tiếng Trung lỗi.

## Bản sửa

- Cache vẫn dùng key theo transcript để giữ lại WAV đang có và tránh gọi lại TTS.
- File upload mới dùng
  `generated/poi-{id}/{language}/{sha256(text)}-{sha256(wav)}.wav`.
- Storage kiểm tra audio hash trong tên file khớp bytes trước khi gọi mạng;
  vẫn kiểm tra MIME và bytes public, vẫn `x-upsert=false`.
- URL Ready cũ và mới đều được nhận diện theo đúng POI, ngôn ngữ và text hash.
  Không ghi đè/xóa file cũ, không cần migration hay import lại dữ liệu.
- Translation được lưu trước TTS như cũ. WAV được cache trước upload như cũ.
  Retry sau lỗi upload hoặc SQL dùng lại cache và cùng object key mới. File
  upload thành công nhưng mất response được kiểm tra lại thay vì upload lại.
- Khóa trong process và giới hạn một pipeline hiện tại được giữ nguyên.
  Không thêm hỗ trợ nhiều instance hoặc cơ chế đồng bộ database giữa máy.

## Kiểm thử và giới hạn

Test dùng SQLite in-memory, cache riêng và HTTP stub; không dùng database/bucket
bàn giao. Có ca source vi và ngoại ngữ zh-hans, cache có/không có, file legacy
khác bytes, hai rendition khác nhau, Ready legacy/mới, mất response upload,
lỗi persistence, sai MIME/bytes/hash và đồng thời cùng POI.

Kết quả: toàn bộ 261 test đạt, không bỏ qua test nào, bằng lệnh
`dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-restore --verbosity minimal`.
Docker build Release thành công, 0 warning/0 error. Image mới mang tag
`autodubbing-api:dev`, digest
`sha256:b560c78f07666b8c567adaeaaa0465ba46dc60e140053ca6d1e9aa4b74183199`.
Kết thúc lượt sửa source, container chưa được recreate nên còn dùng digest
9155eb...; bước triển khai và kiểm chứng thật sau đó được ghi ở cuối tài liệu.

Trong lượt chẩn đoán ban đầu, chỉ GET audio public và đọc SQL/cache, không gọi
Gemini/TTS hoặc POST upload Supabase. Lượt kiểm chứng thật sau đó được người
dùng cho phép riêng và được ghi ở cuối tài liệu. Không chạy batch 15 ngôn ngữ x 8 POI.

## Áp dụng vào Docker

Build từ source (PowerShell tại thư mục repository):

```powershell
docker build -t autodubbing-api:dev .
```

Build không thay container đang chạy. Khi không có job đang xử lý, recreate
chỉ API (PowerShell):

```powershell
cd D:\AutoDubbing-Handover
docker compose --env-file .env.team -f compose.handover.yaml up -d --no-deps --no-build --force-recreate api
```

Giữ nguyên env và volumes. Queue/job trong RAM mất khi recreate, cache và SQL
giữ lại. Không cần restart SQL/user-web hoặc đổi bucket. Không nạp lại TAR cũ
vì sẽ đưa tag API về bản bàn giao cũ. Không dùng `down -v` hay reset Docker.

Kiểm tra image thực của container so với tag vừa build:

```powershell
docker inspect autodubbing-handover-api-1 --format '{{.Image}}'
docker image inspect autodubbing-api:dev --format '{{.Id}}'
```

Sau cập nhật, không chủ động mở luồng chọn vị trí/ngôn ngữ thiếu để test cho tới
khi đã thống nhất phạm vi gọi provider thật. Có thể dùng GET health/OpenAPI và
đọc audio Ready riêng trước; việc đó không chứng minh generation đã thành công.

## Triển khai và kiểm chứng thật đã được cho phép

Ngày 26/10/2026, sau khi người dùng cho phép một cặp POI/ngôn ngữ:

- Đã recreate chỉ container `autodubbing-handover-api-1`, dùng image mới
  `sha256:b560c78f07666b8c567adaeaaa0465ba46dc60e140053ca6d1e9aa4b74183199`.
  API health Online, web cổng 18081 trả HTTP 200. Không đổi env/key, SQL,
  volume, tài khoản hoặc bucket. Không cần build lại vì tag đã đúng bản sửa.
- Thực hiện qua trình duyệt Edge do Playwright điều khiển trên trang Docker
  thật `http://localhost:18081`, 21:04:47–21:05:34 giờ Việt Nam. Không dùng
  mock cho API, audio hoặc provider. Hai bộ điều khiển giao diện có sẵn lỗi
  khởi tạo nên dùng Playwright của dự án để thực hiện các thao tác web thật.
- Audio có sẵn: Nhà thờ Đức Bà / vi, 32,92 giây. Bấm Phát trên web, xác nhận
  currentTime vượt 1 giây, paused=false, không có media error; sau đó bấm dừng.
- Cặp sinh mới duy nhất: Bưu điện Trung tâm Sài Gòn, POI 2 / en. Trước test
  không có translation/audio; API trả fallback vi và audio=null.
- Chọn en ở dropdown trên Nhà thờ (en đã Ready), quay lại danh sách và chọn
  Bưu điện để giữ lựa chọn en, tránh vô tình tạo thêm audio nguồn vi.
  Không dùng GPS/chọn vị trí hoặc chuẩn bị lân cận.
- Đúng một POST `/api/pois/2/prepare-content`, sau đó polling chuyển
  Queued → Processing → Ready; job `1444124b-2589-4c96-8f5b-b7137c5275c2`.
  Hàng rào chặn POST ngoài phạm vi không phải chặn yêu cầu nào (blocked=[]).
- Văn bản kết quả là tiếng Anh, mô tả Bưu điện, actualLanguage=en,
  isFallback=false; audio en 36,6 giây phát được (currentTime>1s,
  paused=false, media error=null). Không gặp SupabaseObjectMismatch.
- SQL sau test: 20 bản dịch / 27 audio, tăng đúng 1 mỗi loại. Bản dịch id 1017,
  audio id 1024 cùng POI 2 / en / Ready. Có đúng 1 tài khoản Admin.
- Tải lại trang, chọn lại Bưu điện / en qua UI. Audio phát được, cùng URL
  có text hash `f2593c15...` và audio hash `79ba620f...`. Tổng số POST tạo nội
  dung xuyên suốt lượt thử vẫn là 1; không có POST sau reload, không job mới.
  GET content không gọi provider nên lượt reload không chạy lại dịch/TTS.
- Logger HTTP của Gemini/Supabase được tắt bằng RemoveAllLoggers để bảo vệ
  dữ liệu. Không có số đếm trực tiếp provider trong log; xác minh việc tái sử
  dụng dựa trên request của trình duyệt, trạng thái Ready, URL và SQL.

Script live chỉ dùng một lần, ảnh chụp và JSON của lượt kiểm chứng đã được
dọn sau khi hoàn tất. Kết quả và phạm vi kiểm chứng được giữ trong tài liệu
này; test hồi quy trong AutoDubbing.Tests vẫn được giữ để kiểm tra lại bằng
dữ liệu riêng, không cần gọi provider thật.

Kết quả phát xác nhận bằng trạng thái player/thời gian chạy thực của trình
duyệt; chưa đánh giá bằng tai chất lượng phát âm hoặc tính chính xác từng câu.
261 test tự động dùng stub/SQLite ở phần trên là kết quả riêng, không thay
cho lượt kiểm chứng web/provider thật này.
