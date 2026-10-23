# Live content batch — 8 POI × 15 languages

State: Stopped

Codes: O = original Vietnamese; R = Ready; M = missing; X = unavailable/invalid. Each cell: text/audio.

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

Totals: {"translationRows": 4, "translationReadyRows": 4, "audioRows": 4, "audioReadyRows": 4, "verifiedAudio": 4, "storageFiles": 4, "storageBytes": 7265456, "measuredAudioBytes": 7265456, "estimated120Bytes": 217963680, "viTranslationRows": 0}

Stop reason: SupabaseTimeout

Translation and voice quality have NOT been reviewed by a human.

## Listening samples

- Nhà thờ Đức Bà / vi: [Listen](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/vi/a091bc7c5029df494de2ac60dbac63d676bb438ff1851b06ac5787e7383f7496.wav); 32.92s; 1580204 bytes; audio/wav
- Nhà thờ Đức Bà / en: [Listen](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/en/2390eb6b7322243b986afd07caa45a45935c1c0225f5b14e059b70234138a207.wav); 37.64s; 1806764 bytes; audio/wav
- Nhà thờ Đức Bà / ja: [Listen](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ja/fe8928e74157b5712003c57bfa6238e2181d0cdf0e73486428cb4e04f6fdb0f4.wav); 40.60s; 1948844 bytes; audio/wav
- Nhà thờ Đức Bà / ko: [Listen](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ko/f5182a7e0966b7962ca47a46f1d147419445e782b8f2df36ecd011b14ccaa1c5.wav); 40.20s; 1929644 bytes; audio/wav

## Phạm vi và kết quả kiểm kê mới

Đọc trực tiếp SQL Server và liệt kê Supabase trong đợt ngày 23/10/2026, không coi báo cáo cũ là trạng thái hiện tại. Trước chạy: đúng 8 POI Published tiếng Việt, 4 BAN_DICH Ready (en/zh-hans/ja/ko của Nhà thờ Đức Bà), 4 AUDIO Ready (vi/en/ja/ko), 4 object công khai hợp lệ. Không có bản dịch vi dư thừa. Bảy POI còn lại chưa có bản dịch/audio.

Mỗi file Ready được tải không gửi secret/JWT, đối chiếu object, SHA256 của transcript trong URL, MIME, WAV frames và thời lượng SQL. Các bản dịch hiện có được kiểm tra không rỗng, không có lời dẫn rõ ràng; zh-hans/ja/ko có script tương ứng. Với các ngôn ngữ dùng chữ Latin, kiểm tra script không thể chứng minh đúng ngôn ngữ. Văn bản mẫu đầy đủ có trong matrix JSON để đối chiếu; chưa có thẩm định chuyên môn bản dịch/giọng đọc.

## Tiếng Trung

Một request thật dùng nguyên bản dịch Ready, cùng model gemini-3.1-flash-tts-preview, Kore và cmn-CN, trả HTTP 200, finishReason STOP, audio/l16; rate=24000; channels=1. Không có dấu hiệu quota/block trong phản hồi lần này. WAV được cache theo hash transcript để pipeline không gọi lại TTS.

ffprobe xác nhận WAV PCM s16le mono 24 kHz, 33,68 giây, 1.616.684 byte. Không đổi model, tài khoản, key hoặc billing. Lỗi GenerationBlockedOrIncomplete lịch sử không có phản hồi gốc, không thể kết luận nguyên nhân cũ; lần này không tái hiện. Không coi âm thanh cache là Ready trước upload/lưu SQL.

## Dung lượng

Tổng file Supabase thực tế: 4; 7,265,456 byte (6.93 MiB). Ước lượng 120 file dựa trên trung bình 4 audio public đã đo: 217,963,680 byte (207.87 MiB), không phải dung lượng đã tạo. Mẫu hiện tập trung ở một POI và một số ngôn ngữ; độ dài các POI/ngôn ngữ còn lại có thể làm dung lượng thay đổi. Giữ WAV đã kiểm chứng, không thêm dependency chuyển mã, không lưu audio nhị phân vào SQL và không xóa object cũ.

## Source thay đổi và kiểm thử

- AutoDubbing.Service/ContentGeneration/GenerationContracts.cs: thêm chẩn đoán lỗi giới hạn ở model cố định, HTTP status, finish reason trong allowlist và loại quota; không giữ raw provider message.
- AutoDubbing.Service/ContentGeneration/GeminiContentProvider.cs: lấy chẩn đoán an toàn khi quota/HTTP lỗi hoặc generation chưa hoàn tất. Giữ nguyên model, prompt, retry hữu hạn và cooldown.
- AutoDubbing.Service/ContentGeneration/ContentGenerationService.cs: trả chẩn đoán này trong DTO của API Admin, giữ checkpoint và errorCode cũ.
- AutoDubbing.Tests/GeminiProviderTests.cs: 5 ca hồi quy mới cho quota và finish reason, gồm trường hợp dữ liệu provider chứa secret phải bị loại bỏ.
- docs/content-batch-20261023.md: báo cáo này.

Build cuối thành công, 0 lỗi/0 cảnh báo. Suite 200/200 PASS, 0 bỏ qua, dùng HTTP mock và SQLite kiểm thử tách biệt; không gọi dịch vụ thật trong CI. Report: artifacts/test-results/content-batch-20261023/DELL_DESKTOP-1MMSBHT_2026-10-23_15_28_37_net10.0.trx.

Lượt build đầu có xung đột output do hai lệnh build chồng nhau và reference assembly cũ; đã chờ cả hai dừng rồi rebuild tuần tự với --no-incremental, sau đó mới chạy suite trên output đã build thành công.

Không thay schema/entity/migration, CRUD, Session, frontend hoặc CI. Không commit/push/merge. API công khai tiếp tục chỉ đọc nội dung/URL có sẵn; fallback trả actualLanguage và isFallback. API sinh nội dung vẫn cần Admin đang Active, request không token đã được kiểm tra trả 401.

## Tiếp tục có kiểm soát

Runner: artifacts/content-batch-20261023/live_batch.py; lệnh `python artifacts/content-batch-20261023/live_batch.py run`. Chỉ chạy khi muốn tiếp tục thao tác thật và backend đúng workspace đang lắng nghe HTTPS localhost:7239. Đăng nhập trong hộp cục bộ có che mật khẩu; không gửi mật khẩu/JWT vào chat.

Trước khi tiếp tục, chạy `python artifacts/content-batch-20261023/live_batch.py inventory resume` để kiểm kê lại SQL/storage. Runner yêu cầu đúng 8 POI, nguồn giống baseline, dừng nếu thấy dữ liệu nguồn thay đổi hoặc file Ready không hợp lệ. Thứ tự: hoàn tất Nhà thờ Đức Bà trước, rồi 7 POI còn lại. Một ngôn ngữ mới/request (batchSize=1), một tác vụ tại một thời điểm, chờ 35 giây giữa các lượt; vẫn tuân giới hạn endpoint tối đa 3 ngôn ngữ mới và 2 tác vụ đồng thời. Không tạo background worker tự retry vô hạn.

Mỗi lượt lưu progress.json, batch-*.json và sql-checkpoint.json; mỗi POI/final lưu matrix và snapshot object. Giới hạn cứng một lần chạy 3 giờ/120 lượt thiếu, kiểm tra STOP.signal giữa các lượt. 429 được backend retry hữu hạn và tôn trọng Retry-After; daily/zero quota dừng ngay. Runner dừng ở lỗi chưa xử lý, giữ tất cả checkpoint. Không tự thay model hay dùng key/tài khoản khác để né quota.

Bằng chứng gọi lại Ready, khi đã đăng nhập thành công, ở artifacts/content-batch-20261023/repeat-ready-proof.json: phải SkippedReady và toàn bộ SQL/object ID/metadata trước-sau giống nhau; không chỉ so số lượng. Nếu file chưa có thì bước này chưa thực hiện, không được coi là đã chứng minh.

Các file kiểm kê, runner và WAV mẫu nằm trong artifacts (Git bỏ qua), vẫn tồn tại cục bộ. Mật khẩu/JWT chỉ trong bộ nhớ tiến trình; các báo cáo không lưu credentials.

## Bằng chứng gọi lại trên dữ liệu thật

```json
{
  "poiId": 1,
  "language": "vi",
  "sameSqlRows": true,
  "sameStorageObjectIdsAndMetadata": true,
  "skippedReady": true,
  "pass": true
}
```

## Kết quả sau khi đăng nhập Admin thành công

Login và /me HTTP 200; đã dùng đúng API có xác thực. Lần gọi lại vi trả SkippedReady; toàn bộ hàng SQL và object ID/metadata trước-sau giống nhau. Pipeline dùng lại WAV zh-hans từ cache, không dịch hoặc TTS lần nữa, nhưng upload Supabase timeout 30 giây nên không ghi AUDIO Ready. Lần upload chẩn đoán riêng dùng chính WAV đó, timeout hữu hạn 90 giây, gặp URLError sau khoảng 46 giây; đọc bucket public vẫn HTTP 200 khoảng 2,36 giây. Không có bằng chứng lỗi quota Gemini trong đợt này.

Hiện lỗi thuộc kết nối khi gửi file lên Storage; chưa đủ bằng chứng phân biệt lỗi mạng trung gian và phía Supabase. Không giả định nguyên nhân là quota hoặc tự đổi model/billing. Dừng sau hai lần thử upload (một qua API, một chẩn đoán), không lặp upload liên tục; chưa chuyển sang các ngôn ngữ/POI tiếp theo. Đã kiểm kê lại sau cả hai lần để đối chiếu file có thực sự tồn tại hay không.

WAV cache giữ nguyên để tiếp tục khi đường upload hoạt động. Có thể kiểm tra kết nối upload Supabase, mạng/proxy hoặc trạng thái Storage trước; sau đó chạy kiểm kê resume và runner như trên. Phần chẩn đoán không ghi SQL. Nếu upload đã tồn tại ở lần sau, đối chiếu public URL/WAV trước khi tiếp tục ghi metadata qua pipeline; không coi riêng HTTP POST lỗi là bằng chứng object chắc chắn không tồn tại.

Chẩn đoán từng bước (không chứa key/header/JWT):

```json
{
  "atUtc": "2026-10-23T09:12:00.898784+00:00",
  "bytes": 1616684,
  "path": "generated/poi-1/zh-hans/5f063aaa02071ea7f45a41518a3f812c62de1d32274c9f72754fe2be6ca3424d.wav",
  "stages": [
    {
      "stage": "bucket",
      "seconds": 2.3589999999967404,
      "http": 200,
      "public": true
    },
    {
      "stage": "upload",
      "seconds": 46.40700000000652,
      "errorClass": "URLError"
    }
  ]
}
```

## Trạng thái runner cuối

```json
{
  "state": "Stopped",
  "stopReason": "SupabaseTimeout",
  "authentication": {
    "adminId": 1,
    "username": "Hiu",
    "status": "Active",
    "loginHttp": 200,
    "meHttp": 200
  },
  "failure": {
    "language": "zh-hans",
    "status": "Failed",
    "textReady": true,
    "audioReady": false,
    "errorCode": "SupabaseTimeout",
    "audioDurationSeconds": null,
    "failureDetails": null
  },
  "updatedAtUtc": "2026-10-23T09:17:53.510346+00:00"
}
```


Báo cáo chẩn đoán upload và ma trận kiểm kê mới: [storage-upload-20261023.md](storage-upload-20261023.md). Bản source mới đã qua 213 test; điểm nghẽn gửi body vẫn chưa được gỡ.


Cập nhật tiếp theo: [kết quả dùng hostname Storage trực tiếp và ma trận mới](storage-direct-20261023.md). Upload thật qua direct hostname đã thành công; xem báo cáo mới cho số liệu hiện tại.
