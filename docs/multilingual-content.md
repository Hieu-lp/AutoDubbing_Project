# Nội dung và audio đa ngôn ngữ

**Yêu cầu hiện hành 24/10/2026:** dừng sinh sẵn 8 POI × 15 ngôn ngữ. Khách ẩn danh được POST chuẩn bị audio nguồn trong 500 m hoặc yêu cầu một ngôn ngữ của một POI; GET chỉ đọc. Xem hợp đồng đầy đủ, hàng đợi, quota và quy tắc phát 50 m/3 giây tại [content-on-demand.md](content-on-demand.md). Báo cáo live bên dưới là các mốc lịch sử, không phải kiểm kê hôm nay. Cấu hình hiện tạm dừng nhận sinh mới vì quota, Ready vẫn phục vụ.

## Phạm vi và quyết định đã chốt

Backend hỗ trợ 15 ngôn ngữ cho cả văn bản và audio, bao gồm tiếng Việt gốc; vi/en không giới hạn sản phẩm. Danh mục duy nhất ở AutoDubbing.Service/Languages/LanguageCatalog.cs; Controller, Service và test dùng chung danh mục. Model cố định gemini-3.1-flash-lite cho dịch và gemini-3.1-flash-tts-preview cho TTS, không tự đổi model hay bật billing. Tiếng Việt dùng nguyên mo_ta_goc, không tạo BAN_DICH tiếng Việt.

| Mã SQL/API | Hiển thị | TTS languageCode |
| --- | --- | --- |
| vi | Tiếng Việt | vi-VN |
| en | Tiếng Anh (Mỹ) | en-US |
| zh-hans | Tiếng Trung giản thể, Quan thoại | cmn-CN |
| ja | Tiếng Nhật | ja-JP |
| ko | Tiếng Hàn | ko-KR |
| fr | Tiếng Pháp | fr-FR |
| de | Tiếng Đức | de-DE |
| es | Tiếng Tây Ban Nha | es-ES |
| ru | Tiếng Nga | ru-RU |
| it | Tiếng Ý | it-IT |
| pt | Tiếng Bồ Đào Nha (Brazil) | pt-BR |
| th | Tiếng Thái | th-TH |
| id | Tiếng Indonesia | id-ID |
| ms | Tiếng Mã Lai | tự nhận diện theo transcript/prompt |
| hi | Tiếng Hindi | hi-IN |

Giọng Kore dùng chung: Google tài liệu hóa giọng đa ngôn ngữ. Mã sản phẩm và locale provider độc lập; không lưu cmn-CN hoặc pt-BR thay mã sản phẩm vào SQL. Input generate không phân biệt hoa/thường, từ chối khoảng trắng, mã ngoài danh mục và mã trùng. Không tự coi en-us là en. Public content tiếp tục chấp nhận culture hợp lệ như trước để giữ tương thích; danh mục giới hạn các ngôn ngữ có thể yêu cầu sinh mới.

Nguồn đối chiếu: [Firebase TTS 3.1, giọng, ngôn ngữ và locale](https://firebase.google.com/docs/ai-logic/generate-speech), [model dịch](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite), [model TTS](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-tts-preview). Trang TTS GenerateContent hiện chủ yếu mô tả model 3.8; không lấy bảng model mới đó làm bằng chứng gọi thật model 3.1.

Tài liệu Google xác nhận TTS cho từng ngôn ngữ trong bảng, bao gồm Malay qua tự nhận diện. Tài liệu Flash-Lite mô tả khả năng dịch chung; không chứng nhận chất lượng cho từng cặp ngôn ngữ. Mock bao phủ cả danh mục nhưng không chứng minh project có quota hoặc bản dịch/phát âm đạt yêu cầu.

**Cập nhật kiểm chứng thật 20/10/2026:** đã chạy pipeline đúng POI Nhà thờ Đức Bà qua JWT Admin, SQL Server và Supabase public. vi/en/ja/ko hoàn tất kỹ thuật; zh-hans có bản dịch Ready nhưng TTS lỗi GenerationBlockedOrIncomplete, đã dừng trước 10 ngôn ngữ còn lại. Chưa nghe thẩm định. Xem [báo cáo đủ 15 ngôn ngữ, URL và bằng chứng không tạo trùng](multilingual-live-verification.md). Smoke test đoạn ngắn trước đó nằm ở gemini-smoke-test.md.

## API

GET /api/languages cho phép ẩn danh, trả mảng code/displayName; không trả model settings hoặc key.

POST /api/admin/pois/{id}/generate-content yêu cầu JWT Admin đang Active. Tài khoản Locked và token thiếu/sai bị từ chối như các API bảo vệ hiện có. Chỉ xử lý POI Published; POI thiếu/ẩn trả 404 khi cấu hình đã sẵn sàng.

Ví dụ body (chỉ chủ động gọi sau khi đã cấu hình và muốn ghi dữ liệu):

~~~json
{
  "languages": ["vi", "en", "ja"],
  "batchSize": 2
}
~~~

languages nhận 1 đến toàn bộ danh mục, không bắt buộc chọn đủ 15. batchSize mặc định 3, hợp lệ 1–3: giới hạn số ngôn ngữ còn thiếu được thử trong lượt này, không phải số lượng phần tử response. Các ngôn ngữ đã Ready không tiêu tốn batchSize. Lượt sau có thể gửi lại cùng danh sách hoặc chỉ các ngôn ngữ chưa hoàn tất.

Response HTTP 200 là đã xử lý một lượt; phải xem trạng thái từng phần tử để biết hoàn thành bao nhiêu:

~~~json
{
  "poiId": 123,
  "results": [
    { "language": "vi", "status": "Completed", "textReady": true, "audioReady": true, "audioDurationSeconds": 13.08 },
    { "language": "en", "status": "PausedQuota", "textReady": true, "audioReady": false, "errorCode": "QuotaExceeded" },
    { "language": "ja", "status": "Deferred", "textReady": false, "audioReady": false }
  ],
  "remainingLanguages": ["en", "ja"]
}
~~~

Đây là ví dụ hợp đồng, không phải kết quả pipeline thật hoặc ID/duration được tự ghi vào SQL.

| status | Ý nghĩa |
| --- | --- |
| Completed | Lượt này hoàn tất văn bản và audio đã upload, lưu Ready |
| SkippedReady | Văn bản và audio Ready đã tồn tại, không gọi model/upload lại |
| Deferred | Chưa thử vì hết batchSize hoặc lượt đang dừng do quota/lỗi chung |
| Failed | Bước bị lỗi, xem errorCode an toàn và textReady/audioReady |
| PausedQuota | Quota/rate limit đã vượt ngưỡng retry, giữ checkpoint để tiếp tục sau |

remainingLanguages gồm tất cả ngôn ngữ chưa có audio sẵn sàng, kể cả Failed cần xử lý thủ công. Không lặp request vô hạn dựa vào HTTP 200. Với lỗi cần xem lại nội dung như TranslationRequiresReview, sửa nguyên nhân trước khi thử lại; có thể chọn các Deferred khác để tiếp tục.

Thiếu cấu hình Gemini/Supabase: 503 trước khi gọi provider hay ghi SQL. Body sai: 400. Mô tả gốc không phải vi, trống hoặc dài quá giới hạn kỹ thuật 6000 ký tự: 422. Không tự cắt mô tả POI thành hai câu như smoke test. Nội dung dài cần thiết kế chia đoạn riêng; chưa tự triển khai hoặc tự tóm tắt.

GET /api/pois/{id}/content?language=... vẫn trả requestedLanguage, actualLanguage, isFallback, text và audio. Thiếu bản dịch Ready trả actualLanguage=vi và isFallback=true; không tính là đã có bản dịch, không dùng để bỏ qua bước dịch. Chỉ trả audio khớp ngôn ngữ thực tế. Khi đã có bản dịch mà audio chưa xong, actualLanguage là ngôn ngữ bản dịch và audio=null.

## Checkpoint, retry và tính nhất quán

Controller chỉ xử lý HTTP/validation; Service điều phối dịch/TTS và batching; Data đọc/ghi SQL, cache file và upload storage. Không thêm bảng, entity hay migration.

1. Đọc nguồn vi và nội dung Ready hiện có. Không tự tạo POI/tài khoản.
2. Nếu thiếu bản dịch, gọi Gemini rồi lưu riêng BAN_DICH Ready. Nếu TTS thất bại, bản dịch vẫn còn để lần sau chỉ làm TTS.
3. Sinh TTS đúng transcript; đọc MIME thật, đóng gói PCM signed 16-bit little-endian mono thành WAV với sample rate thật. Từ chối audio rỗng, sai định dạng hoặc vượt 6 MiB.
4. Cache WAV thành công ở artifacts/audio-cache trong ContentRoot của API trước khi upload. Chạy lại sau lỗi upload/SQL sẽ dùng WAV cache, không gọi TTS lại.
5. Xác minh bucket public, upload audio/wav, sau đó lưu AUDIO Ready với URL HTTPS đầy đủ. Cột thoi_luong lưu số giây làm tròn lên từ frame WAV; response lượt tạo giữ thời lượng thực tế dạng số thập phân.
6. Giữ WAV cache cả sau khi SQL Ready thành công; không tự xóa cache/file đã có. Khi restart, bản dịch/audio Ready trong SQL và cache trên đĩa vẫn dùng lại được. Cần giữ volume cache nếu chạy container; cache sẽ mất nếu filesystem container bị xóa. Theo dõi dung lượng đĩa vì chưa tự dọn cache.

Object path generated/poi-{id}/{language}/{SHA256-UTF8-text}.wav ghép audio với đúng văn bản. Audio do pipeline tạo có hash không khớp văn bản hiện tại sẽ không được public content phục vụ. URL audio Ready cũ do người dùng nhập thủ công vẫn được giữ theo quy tắc hiện có; backend không thể chứng minh transcript của file ngoài pipeline.

Mỗi bước lưu SQL dùng transaction ngắn và kiểm tra nguồn vẫn Published, tiếng Việt và nguyên văn như lúc đọc. Trước khi lưu audio còn đối chiếu bản dịch Ready hiện hành. Không giữ transaction SQL trong lúc gọi dịch vụ ngoài. SQL và Supabase không có transaction chung: nếu upload thành công nhưng lưu SQL lỗi, file có thể đã tồn tại ở storage, WAV cache còn để thử lại. Đường dẫn có hash, kiểm tra object tồn tại/byte khớp và không overwrite cho phép hoàn tất lại an toàn. Upload dùng hostname Storage trực tiếp đã kiểm chứng, không tăng timeout để thay cho sửa kết nối.

Các bản ghi Ready hiện có được coi là đã duyệt, không tự dịch lại. Nếu sửa mô tả gốc bằng SQL, cần quản lý/thu hồi các bản dịch Ready đã lỗi thời; schema hiện không có source revision để tự xác định độ mới của bản dịch cũ. Không tự thêm cột để giải quyết việc này.

Giới hạn hiện hành toàn API instance: một tác vụ pipeline đồng thời, kể cả Admin, và khóa theo POI để tránh tạo trùng. Luồng khách có thêm queue giới hạn 32, khóa theo POI/ngôn ngữ/hash nguồn, ưu tiên yêu cầu chủ động. Đây là mô hình một instance; chưa có khóa phân tán/unique constraint cho nhiều server. Thông số và giới hạn restart tại [content-on-demand.md](content-on-demand.md).

Gemini 429: tôn trọng Retry-After hoặc RetryInfo; nếu thiếu thì backoff có jitter và có chờ. Tối đa 2 retry mặc định (cấu hình 0–3). Quota ngày/giới hạn 0 dừng ngay và báo riêng DailyLimit/ZeroLimit; không gộp DailyOrZeroLimit như báo cáo lịch sử. RateLimit có cooldown ít nhất một phút; queue DailyLimit ít nhất 30 phút; ZeroLimit cần người vận hành kiểm tra quyền/quota rồi restart sau khi khắc phục, không tự retry. Đây không phải dự đoán giờ quota thực sự hồi phục. Xem hợp đồng mới về requiresOperatorAction và retryAt.

Lỗi 401/403/404 không đổi model hay retry. Lỗi 5xx Gemini retry hữu hạn. Timeout mỗi lần gọi mặc định 55 giây, không tự retry timeout. Supabase timeout 30 giây; lỗi upload giữ WAV và không lặp liên tục. Worker chỉ xử lý job đã nhận từ POST; không tự chạy tiếp cả danh mục. Hủy POST sau khi nhận 202 không hủy job dùng chung. Cancel Admin HTTP hoặc dừng worker giữ checkpoint đã ghi xong. Các endpoint Admin đồng bộ và batchSize 1–3 vẫn dành cho thao tác vận hành chủ động; luồng khách mới luôn một ngôn ngữ/job.

Không ghi key trong URL, response, log hoặc exception. HTTP client Gemini/Supabase tắt logger mặc định và không follow redirect. Chỉ trả mã lỗi an toàn, không trả body lỗi provider. Gemini và Supabase credentials chỉ đọc từ cấu hình backend.

## Cấu hình Supabase trước khi sử dụng thật

Supabase đã được người dùng cấu hình và cho phép kiểm chứng thật riêng Nhà thờ Đức Bà; xem báo cáo phía trên. Hướng dẫn dưới đây dành cho môi trường mới: dùng bucket public chỉ chứa audio du khách, URL công khai lưu trong SQL. Không tự tạo project/bucket hoặc bật billing.

1. Tự tạo/chọn project Supabase và bucket public dành cho audio. Giới hạn bucket phù hợp audio/wav và file tối đa 6 MiB. Không tải dữ liệu cá nhân hoặc secrets vào bucket public.
2. Mở file User Secrets cục bộ của AutoDubbing.API qua editor: %APPDATA%\Microsoft\UserSecrets\AutoDubbing-AdminAuth-Local\secrets.json. Giữ nguyên JWT, ORS và Gemini đã có.
3. Bổ sung Supabase:ProjectUrl (HTTPS project URL dạng project-ref.supabase.co), Supabase:Bucket (tên bucket bạn chọn), Supabase:UploadKey (secret key backend hoặc legacy service_role). Không dùng publishable/anon key cho upload này. Tự nhập giá trị trên máy; không gửi key vào chat hoặc truyền key trong command line.
4. Có thể dùng biến môi trường Supabase__ProjectUrl, Supabase__Bucket, Supabase__UploadKey và Gemini__ApiKey trong môi trường triển khai; không ghi giá trị vào Dockerfile/source.
5. Khi muốn dùng bản API mới, chủ động dừng phiên API cũ trong terminal của bạn rồi chạy dotnet run --project AutoDubbing.API --launch-profile https. Lượt kiểm chứng thật đã restart đúng API workspace theo cho phép của người dùng; không dừng các tiến trình dotnet khác.
6. Lấy JWT theo admin-auth.md rồi chủ động gọi generate-content cho POI/ngôn ngữ bạn chọn. Đây là thao tác sẽ gọi provider, upload và ghi BAN_DICH/AUDIO thật, khác với kiểm thử mock đã chạy.

[Supabase public URLs](https://supabase.com/docs/guides/storage/serving/downloads), [upload và x-upsert](https://supabase.com/docs/guides/storage/uploads/standard-uploads), [giữ secret key ở backend](https://supabase.com/docs/guides/getting-started/api-keys), [Gemini quota theo project](https://ai.google.dev/gemini-api/docs/rate-limits).

## Kiểm thử và giới hạn

Test dùng HTTP mock cho Gemini/Supabase và SQLite in-memory có guard provider. Không gọi dịch vụ thật trong CI, không ghi AutoDubbingDb. Các ca mới kiểm tra danh mục 15 ngôn ngữ, dịch 14/nguyên văn vi, TTS locale, batch nhỏ, bỏ qua Ready, retry TTS mà không dịch lại, retry upload từ cache, quota/cooldown và giữ kết quả trước đó, thiếu cấu hình/token, token của Admin Locked, mã sai/trùng, WAV/thời lượng và bucket private.

Lệnh build/test tách output để không đụng API nền (PowerShell tại workspace):

~~~powershell
dotnet build AutoDubbingSystem.slnx --no-restore -p:UseAppHost=false -p:BaseOutputPath=E:\AutoDubbingSystem\artifacts\multilingual-final\
dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore -p:UseAppHost=false -p:BaseOutputPath=E:\AutoDubbingSystem\artifacts\multilingual-final\ --logger trx --results-directory artifacts/test-results/multilingual-final
~~~

Các lệnh build/test không chứa key và dùng mock, không ghi dữ liệu thật. Không thay đổi migration, package version, ORS hoặc cơ chế xác thực. Pipeline thật được xác minh một phần như báo cáo mới; chưa xác minh nhiều instance hoặc nghe thẩm định chất lượng.

## Kết quả triển khai mock trước đợt kiểm chứng thật ngày 20/10/2026

Phần này là mốc lịch sử lúc chưa cấu hình Supabase; trạng thái mới xem multilingual-live-verification.md.

- Build solution output riêng: thành công, 0 lỗi, 0 cảnh báo sau khi restore lấy lại NuGet Audit ngoài sandbox. Không thay package version hoặc tắt audit.
- Toàn bộ suite cuối: 189/189 PASS, 0 bỏ qua; gồm 162 ca có sẵn và 27 ca mới. Report cuối: artifacts/test-results/multilingual-final/DELL_DESKTOP-1MMSBHT_2026-10-20_04_54_42_net10.0.trx.
- Có một lượt trước lỗi timeout ở ca ORS thành công dùng ngưỡng 1 giây trên máy đang tải cao. Chỉ sửa helper test: ca thông thường 10 giây; hai ca kiểm tra timeout vẫn chỉ định 1 giây. Provider ORS và cấu hình timeout production được giữ nguyên.
- SELECT kiểm tra SQL Server: 8 POI, 0 BAN_DICH, 0 AUDIO. Không chạy generate-content trên database thật hoặc upload Supabase thật.
- Git diff thư mục migration, entity và AppDbContext: không thay đổi. Kiểm tra diff whitespace và quét giá trị secrets trong các file thay đổi: PASS.
- Tiến trình API nền PID 12796 vẫn chạy bản trước; chưa restart để nạp endpoint mới.

## Các file trong đợt này

Thêm:

- AutoDubbing.Service/Languages/LanguageCatalog.cs.
- AutoDubbing.Service/ContentGeneration/GenerationContracts.cs, GenerationCoordinator.cs, GeneratedAudioPaths.cs, WavAudio.cs, GeminiContentProvider.cs, ContentGenerationService.cs.
- AutoDubbing.Data/Repositories/ContentGenerationRepository.cs.
- AutoDubbing.Data/Storage/AudioStorageContracts.cs, LocalAudioCache.cs, SupabaseAudioStorage.cs.
- AutoDubbing.API/Controllers/ContentGenerationController.cs, LanguagesController.cs.
- AutoDubbing.Tests/ContentGenerationHttpStub.cs, ContentGenerationTests.cs, GeminiProviderTests.cs, GenerationConcurrencyTests.cs, SupabaseStorageTests.cs.
- docs/multilingual-content.md.

Cập nhật có mục đích, giữ các thay đổi sẵn có:

- AutoDubbing.API/Program.cs: đăng ký các dependency mới; appsettings.json: timeout/retry Gemini không chứa key.
- AutoDubbing.Service/Pois/PoiReadService.cs: audio pipeline có hash phải khớp văn bản hiện hành.
- AutoDubbing.Tests/AdminApiFactory.cs: Gemini/Supabase HTTP mock, cấu hình giả, cache test tách biệt; OrsDirectionsProviderTests.cs: tách ngưỡng thời gian ca parse và ca timeout.
- docs/backend-demo.md và docs/gemini-smoke-test.md: dẫn tới phạm vi đa ngôn ngữ mới, phân biệt smoke test vi/en với khả năng sản phẩm.

Không sửa Dockerfile/CI, scripts smoke test, các migration hoặc các thay đổi khác có sẵn trong workspace. Không commit/push/merge.
Kiểm tra package vulnerable gồm dependency gián tiếp sau restore, chạy ngoài sandbox: cả bốn project không có package được nguồn NuGet hiện tại báo có lỗ hổng.
