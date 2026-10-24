# Chẩn đoán upload Supabase — 23/10/2026

> Báo cáo lỗi lịch sử trước khi kiểm chứng [hostname Storage trực tiếp](storage-direct-20261023.md); không phải tình trạng hiện hành. Batch 120 đã bị hủy theo yêu cầu mới. Hợp đồng backend hiện tại: [content-on-demand.md](content-on-demand.md).

**Chưa hoàn tất tại thời điểm báo cáo này: upload WAV thật vẫn bị kẹt ở giai đoạn ghi request body.** Không ghi AUDIO Ready mới hoặc chạy Gemini tiếp khi upload còn lỗi.

Đây là kiểm kê mới sau chẩn đoán, không lấy số liệu cũ làm trạng thái hiện tại. Tất cả SQL/object trước-sau giữ nguyên; cache WAV tiếng Trung còn nguyên.

## Kết nối và phạm vi kết luận

- Project URL đã kiểm tra: `https://ktnefjccvscpioqbyhus.supabase.co/`, Storage `/storage/v1`, bucket `poi-audio`; đường dẫn object đúng hash transcript hiện hành. Key dạng sb_secret chỉ gửi bằng apikey, không gửi làm Bearer JWT. Không in giá trị key.
- Probe dùng .NET 10.0.12, Windows DESKTOP-1MMSBHT\DELL; đã đối chiếu chủ sở hữu tiến trình API (PID 12948/16072 trước restart). Cùng appsettings Development, User Secrets và biến môi trường theo thứ tự ưu tiên của API; cùng SocketsHttpHandler AllowAutoRedirect=false, HttpClient timeout vô hạn bên ngoài và cancellation hữu hạn cho operation. Đây là tiến trình chẩn đoán riêng, không phải endpoint debug công khai trong API.
- Trong sandbox: DNS được nhưng socket bị AccessDenied/10013, cả URL audio cũ cũng không đọc được. Lỗi này là giới hạn công cụ, không được dùng để kết luận lỗi ứng dụng.
- Ngoài sandbox: DNS trả IPv4 172.64.149.246 / 104.18.38.10; TCP 443 kết nối được; TLS 1.2 xác minh chứng chỉ thành công, issuer Google Trust Services WE1, cipher TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256. Không tắt kiểm tra chứng chỉ. Không thấy IPv6 trong kết quả DNS của probe; không ép IPv4 hoặc đổi DNS toàn máy.
- Đọc audio cũ không có secret/JWT trả HTTP 200, MIME audio/wav, 1.580.204 byte, hash khớp kiểm kê. Đọc bucket có xác thực HTTP 200. Object tiếng Trung trước và sau mỗi thử nghiệm đều HTTP 400 với mã Storage **NoSuchKey**, không phải tự coi mọi 400 là lỗi xác thực.
- Proxy môi trường không có HTTP_PROXY/HTTPS_PROXY/ALL_PROXY/NO_PROXY; .NET không trả endpoint proxy cụ thể. Một thử nghiệm UseProxy=false trong riêng probe vẫn thất bại; không sửa cấu hình proxy hệ thống hoặc production.

## Các lần upload có giới hạn

Mỗi lần kiểm tra object trước; x-upsert=false, cùng WAV 1.616.684 byte và Content-Type audio/wav, Content-Length chính xác. Không tạo file thử khác hoặc ghi đè object cũ.

| Probe | Giới hạn | Thời gian lỗi | Byte đã ghi vào stream .NET | HTTP upload | Object sau thử |
| --- | --- | --- | --- | --- | --- |
| host | 30s | 30.08s | 917,504 / 1.616.684 | Chưa nhận | NoSuchKey |
| host-direct | 30s | 30.23s | 786,432 / 1.616.684 | Chưa nhận | NoSuchKey |
| host-90 | 90s | 90.08s | 1,179,648 / 1.616.684 | Chưa nhận | NoSuchKey |

Các EventSource có RequestContentStart nhưng không có RequestContentStop của upload. Timeout xảy ra khi ghi body, không phải chỉ chờ response sau khi gửi hết file. Byte đã ghi là số ứng dụng ghi vào stream, không phải chứng minh server đã nhận/ACK đầy đủ.

Exception chain ngoài sandbox: TaskCanceledException → TaskCanceledException → HttpRequestException (HttpRequestError Unknown) → IOException → SocketException (OperationAborted, native 995). Mã 995 ở đây phát sinh khi cancellation hủy operation; không được coi riêng mã này là bằng chứng firewall chặn. Không có HTTP response upload nên chưa có mã Storage để quy lỗi xác thực/quota/file limit hay phía dịch vụ.

Đã khoanh vùng được điểm nghẽn ở luồng gửi HTTP body qua TLS trong môi trường host. **Chưa xác định được thành phần mạng/flow control/trung gian hay phía nhận cụ thể gây nghẽn.** Các thử nghiệm chưa chứng minh lỗi nằm trong Gemini, quota, DNS, TLS hoặc proxy. Lượt 90 giây cũng thất bại nên không coi tăng timeout là cách sửa; production giữ mặc định 30 giây.

## Source đã sửa trong đợt này

- AutoDubbing.Data/Storage/SupabaseAudioStorage.cs: GET public kiểm tra object trước POST; nếu bytes/MIME khớp cache thì dùng lại, không upload. Object khác bytes bị từ chối, không ghi đè. Upload x-upsert=false; conflict chỉ chấp nhận sau khi GET xác minh cùng bytes. Sau POST, phải GET public đối chiếu bytes/MIME rồi mới trả URL để Service lưu Ready.
- Cùng file: đo giai đoạn CheckingBucket/CheckingExistingObject/Uploading/WaitingForUploadResponse/VerifyingPublicObject, BytesWritten, TotalBytes, elapsed và HTTP status; đọc mã Storage theo allowlist, phân biệt bucket/quyền/MIME/giới hạn file/dịch vụ. Không giữ raw body, error message, header hoặc secret. Ghi stream có Content-Length và truyền CancellationToken vào từng write. Caller cancellation vẫn propagate.
- AutoDubbing.Data/Storage/AudioStorageContracts.cs: DTO chẩn đoán và chuỗi loại exception/HResult/HttpRequestError/SocketError/native code an toàn.
- AutoDubbing.Service/ContentGeneration/GenerationContracts.cs và ContentGenerationService.cs: đưa storageFailureDetails vào kết quả API Admin; giữ checkpoint, batch và xác thực.
- AutoDubbing.Tests/SupabaseStorageTests.cs, ContentGenerationHttpStub.cs, ContentGenerationTests.cs: mock object public; regression cho object tồn tại/khác bytes, mất response sau upload, HTTP conflict, mã lỗi 400 khác nhau, timeout, caller cancellation, không lưu Ready khi file chưa đọc được và resume không gọi lại TTS/upload.

Không đổi model/prompt Gemini, schema/entity/migration, POI/mô tả, CRUD, Session, frontend, bucket/quyền/key, firewall/antivirus hoặc DNS hệ thống. Không commit/push/merge. Những thay đổi GeminiContentProvider/GeminiProviderTests có sẵn từ đợt trước được giữ nguyên.

## Kiểm thử và API local

Build solution: thành công, 0 cảnh báo/0 lỗi. Suite sau thay đổi cuối: **213/213 PASS**, 0 bỏ qua; gồm 13 ca mới so với 200 trước đó. Test dùng mock HTTP và SQLite riêng, không gọi provider thật hoặc ghi SQL dự án.

Report: artifacts/test-results/storage-upload-20261023/DELL_DESKTOP-1MMSBHT_2026-10-23_16_44_39_net10.0.trx. Probe cũng build thành công 0 cảnh báo/0 lỗi. Chỉ dừng API đã đối chiếu đúng workspace/output, nạp bản storage-upload-build mới; local HTTPS localhost:7239 và HTTP localhost:5133. Không tự gọi lại upload từ API sau khi ba probe đã thất bại; bản mới hiện có guard/diagnostics để dùng khi kết nối được xử lý.

## Ma trận mới: văn bản/audio

O = mô tả Việt gốc; R = Ready và audio đã xác minh; M = thiếu. WAV tiếng Trung cache chưa upload không tính Ready.

| POI | vi | en | zh-hans | ja | ko | fr | de | es | ru | it | pt | th | id | ms | hi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 Nhà thờ Đức Bà | O/R | R/R | R/M | R/R | R/R | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 2 Bưu điện Trung tâm Sài Gòn | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 3 Dinh Độc Lập | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 4 Bảo tàng Chứng tích Chiến tranh | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 5 Chợ Bến Thành | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 6 Nhà hát Thành phố | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 7 Phố đi bộ Nguyễn Huệ | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 8 Thảo Cầm Viên Sài Gòn | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |

SQL: 4 bản dịch Ready (không tạo bản dịch vi); 4 audio Ready. Supabase: 4 file, 7,265,456 byte = 6.93 MiB. Thiếu 108 bản dịch và 116 audio.

Ước lượng 120 file từ 4 audio public thực tế: 217,963,680 byte ≈ 207.87 MiB; chưa tính là dung lượng đã tạo. Cache riêng: 1 WAV tiếng Trung 1.616.684 byte, 33,68 giây, PCM s16le mono 24 kHz. Không đổi đuôi WAV thành MP3 hoặc thêm dependency chuyển mã.

Đã đối chiếu 120 ô ma trận qua API public mới, URL audio hiện có đọc không cần secret/JWT; không có lỗi đối chiếu. Toàn bộ snapshot SQL/object trước-sau chẩn đoán giống nhau. Bằng chứng gọi lại vi SkippedReady từ lượt đăng nhập thành công trước đó vẫn được giữ ở repeat-ready-proof.json; không tuyên bố đã chạy thành công phép thử gọi lại zh-hans trên dữ liệu thật, vì zh-hans chưa upload được. Các tình huống mất response và resume không trùng đã chứng minh bằng mock/SQLite, không thay cho kết quả upload thật.

## Thao tác bên ngoài workspace để gỡ chặn

1. Thử cùng máy trên một mạng khác (ví dụ hotspot), giữ nguyên cấu hình ứng dụng/key/bucket; không đổi DNS, không tắt firewall/antivirus. Chạy lại probe trong Terminal Windows của bạn, ngoài môi trường agent, để đối chiếu độc lập:

```powershell
dotnet run --project artifacts/storage-network-probe/StorageProbe.csproj --no-build -- host-user-network --upload
```

Probe đọc object trước, chỉ gửi cùng WAV cache nếu NoSuchKey; giới hạn 30 giây và không ghi SQL. Báo cáo probe-host-user-network.json nằm trong artifacts/storage-network-probe. Nếu file đã tồn tại, probe chỉ đọc/so bytes và không POST. Không gửi secrets.json hoặc header trong ảnh/log.

2. Trong Supabase Dashboard của project hiện tại, xem Storage logs theo POST /storage/v1/object/poi-audio/generated/poi-1/zh-hans/...wav tại các startedAtUtc/finishedAtUtc trong probe-host*.json; giờ Việt Nam/Thái Lan = UTC+7. Nếu không có request tới Storage, đối chiếu mạng/proxy/endpoint security và lưu lượng upload. Nếu có request, lấy HTTP/status/error code và request ID đã che token để khoanh vùng phía dịch vụ. Không xoay key/đổi quyền bucket để thử.

3. Có thể đối chiếu upload đúng WAV cache bằng giao diện Storage trên browser đang đăng nhập project. Chọn đúng folder generated/poi-1/zh-hans và tên hash hiện có; dừng nếu object đã tồn tại, không tạo tên khác hoặc ghi đè file chưa rõ nguồn. Browser cũng lỗi thì nghiêng về đường mạng/provider; browser thành công nhưng .NET lỗi thì đối chiếu cấu hình/nhật ký của endpoint security/proxy theo ứng dụng, không tắt chúng.

4. Khi probe xác minh uploadedMatchesCache=true hoặc existingMatchesCache=true, chạy kiểm kê lại rồi runner Admin. Bản storage mới sẽ dùng lại object/cache, chỉ còn lưu SQL khi đúng nguồn và transcript. Runner cần đăng nhập trong hộp Windows, không gửi mật khẩu vào chat:

```powershell
python artifacts/content-batch-20261023/live_batch.py inventory resume
python artifacts/content-batch-20261023/live_batch.py run
```

Sau đó kiểm tra zh-hans Ready/public WAV và gọi lại để chứng minh không tăng SQL/object trước khi tiếp tục Nhà thờ Đức Bà đủ 15 rồi 7 POI còn lại. Giữ batchSize=1 hiện tại, tối đa 2 tác vụ, backoff/retry hữu hạn và dừng khi quota/lỗi nối lại. Không gọi Gemini khi upload chưa hoạt động; không dịch/TTS lại WAV đang cache.

## Nghe và đối chiếu

- Nhà thờ Đức Bà / vi: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/vi/a091bc7c5029df494de2ac60dbac63d676bb438ff1851b06ac5787e7383f7496.wav), 32.92s, 1,580,204 byte.
- Nhà thờ Đức Bà / en: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/en/2390eb6b7322243b986afd07caa45a45935c1c0225f5b14e059b70234138a207.wav), 37.64s, 1,806,764 byte.
- Nhà thờ Đức Bà / ja: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ja/fe8928e74157b5712003c57bfa6238e2181d0cdf0e73486428cb4e04f6fdb0f4.wav), 40.60s, 1,948,844 byte.
- Nhà thờ Đức Bà / ko: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ko/f5182a7e0966b7962ca47a46f1d147419445e782b8f2df36ecd011b14ccaa1c5.wav), 40.20s, 1,929,644 byte.

Chưa thẩm định chất lượng dịch hoặc giọng đọc; HTTP 200 chỉ là kiểm tra kỹ thuật.

## Bằng chứng cục bộ

- artifacts/storage-network-probe/probe-sandbox.json, probe-host.json, probe-host-direct.json, probe-host-90.json, probe-host-metadata.json.
- artifacts/storage-network-probe/final-audit.json.
- artifacts/content-batch-20261023/sql-upload-preflight.json, storage-upload-preflight.json, sql-final.json, storage-final.json, matrix-final.json.
- artifacts/content-batch-20261023/progress.json và repeat-ready-proof.json.

## B? sung ??i chi?u m?ng v? multipart

Ng??i d?ng x?c nh?n probe host-user-network ch?y sau khi ??i hotspot/m?ng kh?c; ch?a th? upload qua Dashboard. L??t Terminal b?n ngo?i agent c?ng timeout, n?n kh?ng ch? l? gi?i h?n sandbox. Ch?a ?? b?ng ch?ng ch? ??ch danh endpoint security, giao th?c .NET hay ph?a Supabase.

| L??t | Byte WAV ghi v?o stream | L?i sau | Object sau |
| --- | --- | --- | --- |
| host-user-network | 262,144/1,616,684 | 30.10s | NoSuchKey |
| host-multipart | 393,216/1,616,684 | 30.06s | NoSuchKey |

Multipart ch? l? probe theo c?ch [SDK Supabase x? l? Blob](https://github.com/supabase/storage-js/blob/master/src/packages/StorageFileApi.ts), kh?ng ??i uploader production. Gi? c?ng WAV, key, bucket, endpoint, timeout 30 gi?y v? x-upsert=false. C? raw body v? multipart ??u ch?a th?nh c?ng. Kh?ng g?i l?i Gemini, kh?ng ghi SQL. Kh?ng c? ph?p upload n?o trong CI.

B?ng ch?ng m?i: artifacts/storage-network-probe/probe-host-user-network.json, probe-host-multipart.json, network-comparison.json.

??i chi?u ti?p: Supabase Dashboard > project hi?n t?i > Storage > poi-audio > generated/poi-1/zh-hans. N?u thi?u folder zh-hans, t?o ??ng folder ?? trong bucket hi?n c?. Upload ??ng WAV cache v?i t?n hash, kh?ng b?t ghi ??, kh?ng ??i t?n/file type. Th? m?c cache tr?n m?y: `E:\AutoDubbingSystem\AutoDubbing.API\artifacts\audio-cache\generated\poi-1\zh-hans`. Khi upload th?nh c?ng, ch?a t?nh AUDIO Ready: c?n x?c minh object/bytes/public URL r?i ch?y pipeline Admin ?? l?u SQL; backend m?i d?ng l?i object n?n kh?ng upload/d?ch/TTS l?i. N?u browser c?ng l?i, l?u m? HTTP/error Storage ho?c request ID ?? che credentials ?? ??i chi?u logs, kh?ng g?i key/Authorization.


Cập nhật tiếp theo: [kết quả dùng hostname Storage trực tiếp và ma trận mới](storage-direct-20261023.md). Upload thật qua direct hostname đã thành công; xem báo cáo mới cho số liệu hiện tại.
