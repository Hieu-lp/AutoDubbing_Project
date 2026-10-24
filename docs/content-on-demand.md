# Nội dung theo vị trí và ngôn ngữ được chọn

Yêu cầu hiện hành ngày 24/10/2026. Tài liệu này thay thế kế hoạch sinh sẵn 8 POI × 15 ngôn ngữ và quy tắc cũ chỉ Admin mới có thể kích hoạt pipeline. Không chạy tiếp batch 120 audio. Khách dùng ẩn danh; Gemini/Supabase key chỉ ở backend. Không thêm frontend, bảng, cột hoặc migration.

## Phạm vi và trạng thái triển khai

- POST vị trí chỉ chuẩn bị audio **ngôn ngữ nguồn** cho POI `Published` trong 500 m, gần nhất trước, hòa khoảng cách thì ID nhỏ trước. Hiện pipeline hỗ trợ nguồn `vi`, dùng nguyên mô tả gốc, không tạo bản dịch tiếng Việt. Nguồn khác hoặc mô tả rỗng/quá 6000 ký tự trả `UnsupportedSourceContent`.
- Chỉ POST chọn ngôn ngữ cụ thể mới yêu cầu ngoại ngữ của một POI. Không tự xếp 14 bản dịch khi nhận vị trí. Danh mục 15 mã giữ nguyên trong [LanguageCatalog](../AutoDubbing.Service/Languages/LanguageCatalog.cs).
- Ready hiện hành được trả ngay, không gọi provider/upload. Thành công lưu SQL và URL để mọi khách dùng chung. Tái sử dụng bản dịch Ready và WAV cache; kiểm tra object trước upload, giữ hostname Storage trực tiếp đã kiểm chứng. Chỉ Ready sau xác minh upload và lưu SQL thành công.
- Không xóa POI, mô tả gốc, bản dịch, audio, cache hay object có sẵn. Pipeline hiện giữ WAV cache cả sau khi lưu Ready; chưa có tự dọn cache, cần theo dõi dung lượng đĩa. Không đổi định dạng WAV hoặc thêm chuyển mã.
- Không chạy dịch vụ thật trong đợt triển khai này. Các số liệu Nhà thờ Đức Bà đủ 15 ngôn ngữ trong [báo cáo Storage trực tiếp](storage-direct-20261023.md) là bằng chứng lịch sử, không phải kiểm kê lại hôm nay. Dữ liệu đó được giữ nguyên.

**Công tắc tạm dừng:** [appsettings.json](../AutoDubbing.API/appsettings.json) hiện đặt `ContentPreparation:AcceptNewGeneration=false` vì lần chạy thật gần nhất bị quota chặn. Ready vẫn trả 200; nội dung thiếu trả 503 với `GenerationPaused`, không nhận job. Sau khi người vận hành xác minh quota/quyền model, đặt `ContentPreparation__AcceptNewGeneration=true` trong môi trường backend và khởi động lại để nhận công việc. Không tự bật billing, đổi tài khoản/key/model hoặc gọi thử Gemini để dò quota. Endpoint Admin cũ vẫn được bảo vệ và là công cụ vận hành chủ động; công tắc này chỉ điều khiển luồng khách.

## Hợp đồng API ẩn danh

Ví dụ request để gọi thủ công trong [content-preparation.http](../content-preparation.http). Không đưa key/JWT vào frontend. Mọi GET chỉ đọc và không xếp hàng.

| Endpoint | Request / kết quả |
| --- | --- |
| `GET /api/languages` | Danh mục mã; generation chỉ nhận đúng một mã này, không tự ánh xạ `en-US` thành `en` |
| `GET /api/content-preparations/settings` | `preparationRadiusMeters:500`, `playbackRadiusMeters:50`, `dwellSeconds:3`, `pollAfterSeconds:3` |
| `POST /api/pois/prepare-nearby` | `{ "latitude":10.7798, "longitude":106.6990 }`; bắt buộc tọa độ hữu hạn, vĩ độ −90..90, kinh độ −180..180. Server chọn bán kính/ngôn ngữ, không lấy radius từ client hoặc sửa radius SQL |
| `POST /api/pois/{id}/prepare-content` | `{ "language":"en" }`; nhận một ngôn ngữ trong danh mục, không phân biệt hoa/thường, không nhận khoảng trắng hoặc mảng ngôn ngữ |
| `GET /api/content-preparations/{jobId}` | Snapshot tác vụ; 200 kể cả trạng thái lỗi; 404 nếu không có/hết thời hạn/instance đã restart hoặc POI không còn Published |
| `GET /api/pois/{id}/content?language=en` | Chỉ đọc nội dung lưu, giữ `requestedLanguage`, `actualLanguage`, `isFallback`, `text`, `audio`. Chưa có bản dịch có thể trả vi fallback; không kích hoạt provider |

POST một ngôn ngữ trả một item. Ví dụ đang chờ:

```json
{
  "poiId": 2,
  "language": "en",
  "sourceVersion": "<sha256 nguồn>",
  "status": "Queued",
  "textReady": false,
  "audioReady": false,
  "jobId": "<uuid>",
  "statusUrl": "/api/content-preparations/<uuid>",
  "errorCode": null,
  "retryAt": null,
  "content": {
    "poiId": 2,
    "requestedLanguage": "en",
    "actualLanguage": "vi",
    "isFallback": true,
    "text": "Mô tả nguồn đã lưu",
    "audio": null
  },
  "distanceMeters": null,
  "requiresOperatorAction": false
}
```

Đây là ví dụ cấu trúc, không phải kết quả dịch thật. `content` có thể là null. Item Ready có `audioReady=true`, `content.audio.url`, `content.actualLanguage` đúng `language` và `content.isFallback=false`. Ready lấy ngay từ SQL không tạo job nên `jobId/statusUrl=null`. Fallback có audio tiếng Việt vẫn không làm `textReady/audioReady` của tiếng Anh thành true.

POST gần trả `{ "preparationRadiusMeters":500, "items":[...] }`. Item có thêm `distanceMeters`; thứ tự gần trước. Mỗi item có trạng thái riêng: khi đầy hàng đợi, có thể một số item đã nhận, một số `QueueFull`. Không rollback công việc đã nhận. Ngoài vùng/không có Published trả 200 với `items:[]`.

| HTTP POST | Ý nghĩa |
| --- | --- |
| 200 | Snapshot không có công việc đang chờ; Ready trả ngay. Với response nhiều item phải đọc từng trạng thái, không coi 200 là mọi item đã Ready |
| 202 | Có item Queued/Processing; `Location` trỏ status URL của một item đang chờ, các item còn lại có status URL riêng. Request không chờ pipeline |
| 400 | Body/tọa độ/ngôn ngữ không hợp lệ |
| 404 | POI yêu cầu đơn lẻ thiếu/Hidden/không Published, không phân biệt nguyên nhân |
| 422 | Nguồn chưa hỗ trợ; không tự sửa mô tả |
| 429 | RequestRateLimited (Problem Details), QueueFull hoặc QuotaExceeded. Khi quota/capacity trả item tương ứng, không có job mới |
| 503 | GenerationPaused/GenerationNotConfigured hoặc provider/storage tạm ngừng |

Response hỗn hợp gần trả 202 nếu có item đang chờ; nếu không còn pending, tất cả quota/full trả 429, tất cả unavailable trả 503 (hoặc 422 nếu tất cả nguồn sai); trường hợp hỗn hợp khác trả 200 và trạng thái từng item. `Cache-Control:no-store` trên POST/status. `Retry-After` cho pending là 3 giây, cho rate/capacity/cooldown là thời gian chờ tối thiểu. Không gửi Retry-After giả cho trường hợp cần người vận hành.

| `status` | Hành động frontend |
| --- | --- |
| Ready | Có thể dùng audio nếu vẫn đúng vị trí/ngôn ngữ/lựa chọn phát |
| Queued / Processing | Poll status URL theo Retry-After, dừng khi hết nhu cầu hoặc trạng thái terminal |
| Missing | Job cũ không còn nội dung SQL tương ứng; GET nội dung rồi POST lại nếu còn nhu cầu |
| QuotaExceeded | Hiển thị chưa có audio, dừng polling/retry tự động liên tục; xem errorCode/retryAt |
| QueueFull | Chưa nhận tác vụ; chờ Retry-After và chỉ gửi lại nếu còn nhu cầu |
| Unavailable / Failed | Hiển thị chưa sẵn sàng, giữ nội dung hiện có; không retry mỗi GPS tick |
| Superseded | Nội dung nguồn thay đổi; bỏ job cũ, GET nội dung hiện tại rồi POST lại nếu còn nhu cầu |

## Hàng đợi, chống trùng và giới hạn

- Một queue singleton có tối đa **32 tác vụ chưa hoàn tất, kể cả đang chạy**, một BackgroundService consumer. Không tự tạo tác vụ lúc khởi động. Request hủy sau khi được nhận không hủy công việc dùng chung. Mỗi job tối đa 600 giây; mọi lời gọi ngoài có cancellation/timeout hữu hạn.
- Hai FIFO: yêu cầu ngôn ngữ chủ động trước, chuẩn bị nguồn sau. POST chủ động cho job nguồn đang chờ sẽ nâng ưu tiên chính job đó. Không ngắt job đang chạy. Nhiều khách yêu cầu cùng khóa dùng chung jobId; không chiếm thêm chỗ. Prefetch có thể chờ lâu khi liên tục có yêu cầu chủ động; chưa có chống starvation.
- Khóa queue = `(poiId, language chuẩn hóa, SHA256([sourceLanguage,name,originalDescription]))`. Kiểm tra phiên bản lại dưới khóa POI trước pipeline. Khóa POI và semaphore provider dùng chung với Admin; **tối đa một tác vụ pipeline toàn instance**, kể cả Admin. Checkpoint SQL dùng transaction ngắn, không giữ transaction chờ Gemini/Supabase.
- POST chung hai endpoint: **8 request/client/phút**, **30 request/instance/phút**, cửa sổ cố định. Tính cả POST cache hit/POI không tồn tại để chặn đọc ghi quá tải; body sai bị MVC từ chối trước admission. Bộ đếm client có giới hạn 4096; GET polling không tiêu permit này. Dùng RemoteIpAddress, không tin client header tự khai; người dùng chung NAT chung hạn mức. Khi đặt reverse proxy phải cấu hình proxy tin cậy tại hạ tầng, không tin mọi X-Forwarded-For.
- GPS là input không đáng tin, không phải bằng chứng có mặt. Rate/capacity hạn chế tài nguyên; không dùng tọa độ để cấp quyền Admin hoặc bỏ giới hạn. Không lưu lịch sử tọa độ khách trong job.
- Mặc định giữ tối đa 256 job terminal trong 30 phút; job không terminal vẫn tính queue capacity. Lỗi từng khóa cooldown 5 phút, không tự requeue. Quota chặn queue toàn instance, các job đang chờ trở thành terminal cùng nguyên nhân, không gọi tiếp provider. Hết cooldown cũng chỉ chạy lại khi có POST mới.
- RateLimit: cooldown ít nhất một phút, tôn trọng khoảng chờ được provider báo trong giới hạn parser. DailyLimit: dừng ngay, cooldown queue ít nhất 30 phút và không ngắn hơn retry delay ghi nhận; không cam kết thời điểm reset. **ZeroLimit**: allowance bằng 0, không coi là hết quota ngày, cần kiểm tra quyền/free tier/model/project. 401/403/404 là lỗi xác thực/quyền/model riêng, không gọi là quota ngày. ZeroLimit và lỗi quyền/model khóa nhận mới đến khi người vận hành khắc phục và restart; `requiresOperatorAction=true`, `retryAt=9999-12-31T23:59:59.9999999+00:00` là sentinel, không phải lịch tự retry.
- Gemini giữ model đã kiểm chứng, retry tối đa cấu hình 0–3 (mặc định 2); không retry quota ngày/0, 401/403/404 hoặc timeout. Không đổi model/key/billing. Chẩn đoán chỉ mã allowlist, không trả body/secret. Supabase upload qua `https://{project-ref}.storage.supabase.co/storage/v1/...`; URL đọc public vẫn giữ project URL, không cần JWT.

### Restart và phiên bản nguồn

Queue/jobId/ưu tiên/rate counters/cooldown chỉ trong bộ nhớ một instance. Restart mất các metadata này; pending không tự phục hồi. SQL Ready, bản dịch checkpoint, WAV trên volume cache và object Storage tồn tại; client gặp 404 phải GET nội dung hiện tại trước, sau đó chỉ POST nếu còn cần và quota cho phép. Công tắc AcceptNewGeneration=false tồn tại trong cấu hình để giữ tạm dừng qua restart; không dùng restart để né quota. Chưa hỗ trợ nhiều replica hoặc khóa phân tán. Chạy đúng một instance.

Schema chưa lưu revision nguồn cho bản dịch cũ. Bản dịch Ready hiện có được coi là đã duyệt; hash job chống trùng và phát hiện nguồn thay đổi trong lúc chờ/chạy, không chứng minh bản dịch lịch sử khớp nguồn đã bị sửa trực tiếp trong SQL. Nếu sau này thêm chỉnh sửa POI, cần thiết kế quản lý revision/invalidation trước; đợt này không đổi nguồn hoặc schema và không tự thu hồi dữ liệu cũ. URL/hash audio managed phải khớp transcript hiện hành. Với file Ready bên ngoài pipeline, backend kế thừa quy tắc tin dữ liệu đã duyệt, không tải và thẩm định lại từng lần cache hit.

## Hợp đồng frontend: chuẩn bị và phát tách riêng

Đọc settings một lần khi bắt đầu phiên tham quan; dùng 500 m, 50 m, 3 giây làm cấu hình ứng dụng, không cập nhật hàng loạt `ProximityRadius` trong SQL. Dùng GET nearby/list để biết POI Published; vị trí chỉ gửi POST khi cần chuẩn bị.

1. Nút **Bắt đầu thuyết minh** xin quyền vị trí và mở khả năng phát audio trong thao tác người dùng. Xử lý quyền bị từ chối, thiết bị không có GPS, vị trí cũ/sai số cao. Frontend đặt ngưỡng sai số và tuổi vị trí đáng tin phù hợp thiết bị; phải hủy dwell khi không đạt ngưỡng. Backend không xác nhận độ tin cậy GPS.
2. Khi POI mới vào vùng chuẩn bị 500 m, gộp một POST prepare-nearby với vị trí mới. Giữ tập POI đã chuẩn bị/đang có job theo lần tham quan, debounce/coalesce các lần vào vùng; **không POST trên từng GPS tick**, không POST lại job đang Queued/Processing. Ra rồi vào lại kiểm tra cache trạng thái trước; không tạo ngoại ngữ tự động.
3. Khi khách chọn một ngôn ngữ của một POI, POST prepare-content đúng ngôn ngữ đó nếu chưa Ready/đang có job. Đổi ngôn ngữ đánh dấu lựa chọn cũ hết hiệu lực; không phát audio của một response cũ vừa về. Không cần hủy job backend vì kết quả dùng chung cho khách khác.
4. Với mỗi cập nhật vị trí đáng tin, tìm POI trong **50 m**. Bắt đầu đo thời gian đơn điệu khi vào; chỉ xét phát sau khi **ở liên tục ít nhất 3 giây**. Ra vùng, mất fix đáng tin, vị trí hết hạn hoặc đổi POI được chọn: hủy bộ đếm và timer chờ; lần vào sau tính lại. Không dùng chỉ setTimeout 3 giây rồi phát mà không kiểm tra vị trí mới nhất.
5. Nếu nhiều POI đủ dwell, chọn gần nhất (hòa thì ID), chỉ một audio đang phát. Không tự bật nhiều audio hoặc chồng tiếng; việc khách chủ động phát thủ công cần hủy ý định autoplay đang chờ. Chọn rõ chính sách dừng/phát tiếp khi đi ra vùng; ít nhất luôn hủy lượt autoplay chưa bắt đầu.
6. Khi polling/POST trả Ready muộn, **kiểm tra lại**: phiên tham quan vẫn hoạt động, vị trí mới và đáng tin, còn trong 50 m đủ dwell, POI vẫn là ứng viên gần nhất, ngôn ngữ/lựa chọn hiện tại còn khớp, không có audio khác, chưa tự phát POI này trong lượt tham quan. Chỉ sau đó mới gọi play. Không dùng fallback làm ngôn ngữ đã chọn hoàn tất; nếu muốn nghe tiếng Việt thay thế phải xin lựa chọn rõ ràng của khách.
7. Đề xuất autoplay tối đa một lần mỗi POI trong một lần tham quan; chỉ đánh dấu sau khi play thực sự thành công. Cho nghe lại/chọn ngôn ngữ thủ công. Nếu play() bị trình duyệt chặn (NotAllowedError), giữ trạng thái chưa phát, hiện nút bấm nghe; không tự gọi play lặp và không lạm dụng audio im lặng để né chính sách.
8. Poll tối thiểu 3 giây theo Retry-After, tối đa khoảng 10 phút phù hợp job timeout; ngừng khi terminal, khách không còn quan tâm, trang dừng/ẩn lâu hoặc hết phiên. 429 quota/zero: dừng retry tự động liên tục, thông báo văn bản vẫn đọc được, cho thử lại sau khi đủ điều kiện. 404 job: GET nội dung để phục hồi sau restart, tránh POST thành vòng lặp. Hạn mức áp dụng cả POST cache hit, nên ưu tiên GET cho nội dung sẵn có.

Pseudocode quyết định phát: `hasFreshTrustedFix && insidePlaybackRadius && continuousDwell >= dwellSeconds && requestedLanguage == ready.actualLanguage && !ready.isFallback && ready.audio && candidateIsNearest && !anotherAudioPlaying && !autoplayedThisVisit`. Kiểm tra ngay trước play và sau các thao tác bất đồng bộ có thể đổi lựa chọn.

## Kiểm chứng

Đợt HTTP thật sau triển khai đã kiểm tra API đang chạy với cờ sinh mới false, Ready/GenerationPaused, nearby hỗn hợp, validation, hạn mức client/global, WAV public và dữ liệu trước/sau. Xem [báo cáo HTTP và 77 request thực tế](content-on-demand-http-20261024.md). Không gọi Gemini, không restart API và không ghi nội dung mới trong đợt đó; các ca nhận job 202/provider vẫn chỉ có test mock.

Test dùng fake provider/HTTP handler và SQLite in-memory có guard; không SQL Server thật, không Supabase/Gemini thật và không chạy batch. Bao phủ Ready không gọi provider, fallback, enqueue đồng thời, khóa nguồn, một consumer, ưu tiên/FIFO, Hidden, ngôn ngữ/tọa độ sai, queue/rate limits, quota ngày/0/quyền và OpenAPI. Các ca pipeline có sẵn kiểm tra WAV/upload/cache/không Ready sớm được giữ lại.

Frontend còn phải kiểm chứng trên thiết bị/trình duyệt thật: nhiễu GPS ở ranh giới, mất vị trí giữa 3 giây, overlap POI, đổi ngôn ngữ khi audio về muộn, phát một lần/visit, phát lại thủ công, autoplay bị chặn, quyền vị trí và phục hồi sau tab nền/restart. HTTP 200 hoặc test mock không thẩm định chất lượng bản dịch/giọng đọc và không chứng minh quota hiện đã phục hồi.

### Kết quả bàn giao 24/10/2026

- Build solution: **0 lỗi, 0 cảnh báo**. .NET SDK 10.0.401; VSTest/xUnit v2, HTTP mock và SQLite in-memory.
- Toàn bộ suite: **246/246 PASS, 0 bỏ qua**. [TRX](../artifacts/test-results/on-demand-final/DELL_DESKTOP-1MMSBHT_2026-10-24_19_51_07_net10.0.trx). Bao gồm kiểm tra OpenAPI và POST → worker → SQL → polling → gọi lại không tăng bản ghi/provider call.
- Build/test ngoài sandbox do MSBuild bị chặn quyền thư mục tạm trong sandbox. Không gọi Gemini/Supabase thật, không ghi SQL Server thật, không kiểm kê lại dữ liệu live hoặc triển khai/restart API thật. Output build tách riêng, không đụng DLL đang phục vụ.
- Đã đặt STOP.signal trong thư mục runner local cũ để ngăn batch tiếp tục. Marker nằm trong artifacts bị Git ignore; không phải cơ chế điều khiển queue mới. Không chạy runner để thử marker.
- Không đổi POI/mô tả/schema, không thêm dependency/queue ngoài, không tạo frontend, không commit/push/merge.

```powershell
dotnet build AutoDubbingSystem.slnx --no-restore --no-incremental -p:UseAppHost=false -p:BaseOutputPath=E:/AutoDubbingSystem/artifacts/on-demand-build/
dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore -p:UseAppHost=false -p:BaseOutputPath=E:/AutoDubbingSystem/artifacts/on-demand-build/ --logger trx --results-directory artifacts/test-results/on-demand-final
```

Các nhóm source thay đổi:

| Nhóm | File và vai trò |
| --- | --- |
| HTTP/config | [Controller](../AutoDubbing.API/Controllers/ContentPreparationController.cs), [Program](../AutoDubbing.API/Program.cs), [appsettings](../AutoDubbing.API/appsettings.json): POST/GET mới, validation, DI, giới hạn cấu hình, tạm dừng nhận sinh mới |
| Chuẩn bị | [Contracts/options](../AutoDubbing.Service/ContentPreparation/PreparationContracts.cs), [service](../AutoDubbing.Service/ContentPreparation/ContentPreparationService.cs), [queue](../AutoDubbing.Service/ContentPreparation/ContentPreparationQueue.cs), [rate gate](../AutoDubbing.Service/ContentPreparation/PreparationRateGate.cs), [worker](../AutoDubbing.API/Background/ContentPreparationWorker.cs): snapshot Ready, chống trùng/ưu tiên, bounded metadata, cooldown |
| Pipeline cũ | [Service](../AutoDubbing.Service/ContentGeneration/ContentGenerationService.cs), [contracts](../AutoDubbing.Service/ContentGeneration/GenerationContracts.cs), [coordinator](../AutoDubbing.Service/ContentGeneration/GenerationCoordinator.cs), [Gemini provider](../AutoDubbing.Service/ContentGeneration/GeminiContentProvider.cs): guard phiên bản dưới khóa POI, giữ WAV, một slot chung, phân biệt DailyLimit/ZeroLimit và giữ lý do khi đang cooldown |
| Test | [API](../AutoDubbing.Tests/ContentPreparationApiTests.cs), [queue/rate](../AutoDubbing.Tests/ContentPreparationQueueTests.cs), [worker](../AutoDubbing.Tests/ContentPreparationWorkerTests.cs); cập nhật [factory](../AutoDubbing.Tests/AdminApiFactory.cs), [Gemini](../AutoDubbing.Tests/GeminiProviderTests.cs), [concurrency](../AutoDubbing.Tests/GenerationConcurrencyTests.cs) |
| Tài liệu | Tài liệu này, [request mẫu](../content-preparation.http), cập nhật [multilingual](multilingual-content.md), [backend demo](backend-demo.md), đánh dấu các báo cáo batch/upload/live là lịch sử |
