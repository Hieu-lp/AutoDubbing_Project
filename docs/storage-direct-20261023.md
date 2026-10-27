# Kết quả tiếp tục pipeline — Storage trực tiếp

> Mốc lịch sử. Từ 24/10/2026 dừng kế hoạch 120 audio, giữ nguyên kết quả đã có và không chạy tiếp runner. Cơ chế hiện hành là chuẩn bị nguồn theo vị trí và ngoại ngữ theo yêu cầu: [content-on-demand.md](content-on-demand.md). Không dùng số liệu lịch sử dưới đây làm kiểm kê mới; chưa gọi Gemini lại để thử quota trong đợt sửa backend.

Báo cáo lúc 2026-10-23T18:45:21.573509+07:00.

Trạng thái runner: **Stopped**. Lý do dừng: QuotaExceeded.

## Điểm nghẽn và cách sửa đã kiểm chứng

Browser upload tiếng Trung đã được đối chiếu: HTTP 200 không gửi secret/JWT, đúng 1.616.684 byte audio/wav, SHA256 khớp cache. API dùng lại object đã có để lưu AUDIO Ready; không upload/dịch/TTS lại. Phép gọi lại zh-hans trả SkippedReady và toàn bộ SQL/object ID/metadata giống nhau. Bằng chứng: artifacts/content-batch-20261023/repeat-1-zh-hans-proof.json.

Lượt tiếp theo tạo bản dịch/WAV tiếng Pháp hợp lệ nhưng đường POST qua project API host bị ConnectionReset/10054 khi đang ghi body (229.376/1.760.684 byte, khoảng 20,78 giây). HTTP/2 và curl Windows cũng không hoàn tất; curl nhận 100 Continue nhưng không có response 2xx cuối, object vẫn NoSuchKey. Không quy lỗi cho Gemini/quota khi chưa có bằng chứng.

Một đối chiếu giữ cùng project/key/bucket/WAV/TLS/timeout/HTTP1.1, chỉ đổi sang hostname Storage trực tiếp, upload thành công HTTP 200 trong **0,864 giây**. Toàn bộ 1.760.684 byte ghi xong khoảng 0,422 giây; GET lại HTTP 200, audio/wav và SHA256 khớp cache. Bằng chứng: artifacts/storage-network-probe/probe-host-fr-storage-direct.json.

Đường API hostname trong môi trường này gặp lỗi gửi body; đường Storage trực tiếp hoạt động. Chưa có log nội bộ provider để chỉ đích danh thành phần gây reset, không khẳng định một lỗi cụ thể của .NET hay toàn bộ Supabase. [Supabase tài liệu hóa hostname Storage trực tiếp](https://supabase.com/docs/guides/storage/uploads/resumable-uploads).

## Source và kiểm thử

- SupabaseAudioStorage.cs: POST file dùng https://<project-ref>.storage.supabase.co/storage/v1/object/...; chỉ suy ra từ cùng project hosted có một DNS label. Không đổi ProjectUrl/key/bucket hay quyền. Nếu cấu hình đã là Storage trực tiếp, giữ nguyên host. URL public lưu SQL và các GET vẫn dựa trên ProjectUrl.
- Giữ kiểm tra object trước retry, x-upsert=false, GET ẩn danh xác minh bytes/MIME trước SQL Ready, stream/cancellation hữu hạn và chẩn đoán đã lọc secret. Timeout mặc định vẫn 30 giây; không dùng tăng timeout để tuyên bố sửa xong.
- SupabaseStorageTests.cs và ContentGenerationHttpStub.cs kiểm tra POST phải đi đúng hostname Storage cùng project, GET/public URL giữ hostname cấu hình. Thêm regression cho trường hợp ProjectUrl đã là direct hostname để tránh gửi key sang host suy ra sai.
- Build: thành công, 0 lỗi/0 cảnh báo. **214/214 test PASS**, 0 bỏ qua; mock HTTP và SQLite riêng, không có dịch vụ thật trong CI. Report: artifacts/test-results/storage-direct-20261023/DELL_DESKTOP-1MMSBHT_2026-10-23_17_52_18_net10.0.trx.
- API local đã được restart từ output storage-direct-build đã kiểm chứng. Không thay POI/mô tả gốc/schema/migration/CRUD/frontend, không commit/push/merge. Không đổi model, tài khoản, key, billing, TLS, DNS/firewall/antivirus hoặc proxy hệ thống.

## Ma trận hiện tại: văn bản/audio

O = gốc Việt; R = Ready (audio có kiểm tra kỹ thuật); M = thiếu. Fallback không tính là bản dịch Ready.

| POI | vi | en | zh-hans | ja | ko | fr | de | es | ru | it | pt | th | id | ms | hi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 Nhà thờ Đức Bà | O/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R | R/R |
| 2 Bưu điện Trung tâm Sài Gòn | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 3 Dinh Độc Lập | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 4 Bảo tàng Chứng tích Chiến tranh | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 5 Chợ Bến Thành | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 6 Nhà hát Thành phố | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 7 Phố đi bộ Nguyễn Huệ | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |
| 8 Thảo Cầm Viên Sài Gòn | O/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M | M/M |

SQL: 14 bản dịch Ready, 15 audio Ready; 0 bản dịch vi dư thừa. Ma trận xác minh 15/120 audio; còn thiếu 98 bản dịch và 105 audio.

Supabase: 15 file, 27,679,380 byte (26.40 MiB). Audio public đã xác minh: 27,679,380 byte. Ước lượng 120 audio từ trung bình các file đã đo: 221,435,040 byte (211.18 MiB), chưa phải tổng dung lượng đã tạo.

Định dạng giữ WAV PCM s16le mono 24 kHz (theo từng file đã đo), không đổi đuôi thành MP3 hoặc thêm dependency chuyển mã. SQL chỉ lưu metadata/URL. Mô tả gốc của cả 8 POI và các bản dịch/audio có sẵn từ baseline giữ nguyên. Không xóa object cũ/không xác định nguồn gốc.

## Gọi lại và checkpoint

- zh-hans: {"poiId": 1, "language": "zh-hans", "sameSqlRows": true, "sameStorageObjectIdsAndMetadata": true, "skippedReady": true, "pass": true}
- fr: {"poiId": 1, "language": "fr", "sameSqlRows": true, "sameStorageObjectIdsAndMetadata": true, "skippedReady": true, "pass": true}

Runner chỉ gọi API Admin, JWT giữ trong bộ nhớ. Public GET không gọi Gemini và trả actualLanguage/isFallback rõ ràng. Một ngôn ngữ mới/request, một tác vụ tại một thời điểm (trong giới hạn 3 ngôn ngữ mới/request và 2 tác vụ). Mỗi lượt có batch JSON và SQL checkpoint; mỗi POI/final có ma trận, listing object và kiểm chứng URL/WAV. Retry provider hữu hạn, tôn trọng quota/backoff/cooldown; không đổi model/key để né quota.

Tiếp tục theo thứ tự Nhà thờ Đức Bà đủ 15 trước rồi 7 POI còn lại. Nếu stopReason có quota ngày, kiểm tra quota thực tế của đúng project/model rồi chạy lại sau khi quota được phục hồi; không tự chờ/lặp vô hạn. Nếu lỗi quota chưa phải ngày hoặc lỗi nội dung, xem failureDetails/storageFailureDetails trong batch JSON để xử lý đúng bước trước khi tiếp tục.

```powershell
python artifacts/content-batch-20261023/live_batch.py inventory resume
python artifacts/content-batch-20261023/live_batch.py run
```

Runner dùng lại bản dịch, cache WAV và object đã có; các phần Ready được bỏ qua. Đăng nhập trong hộp Windows cục bộ khi cần, không gửi credentials vào chat.

## Phần còn thiếu hoặc lỗi

- 2 Bưu điện Trung tâm Sài Gòn / vi: text Original, audio Missing; QuotaExceeded.
- 2 Bưu điện Trung tâm Sài Gòn / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 2 Bưu điện Trung tâm Sài Gòn / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / vi: text Original, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 3 Dinh Độc Lập / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / vi: text Original, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 4 Bảo tàng Chứng tích Chiến tranh / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / vi: text Original, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 5 Chợ Bến Thành / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / vi: text Original, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 6 Nhà hát Thành phố / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / vi: text Original, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 7 Phố đi bộ Nguyễn Huệ / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / vi: text Original, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / en: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / zh-hans: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / ja: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / ko: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / fr: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / de: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / es: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / ru: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / it: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / pt: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / th: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / id: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / ms: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.
- 8 Thảo Cầm Viên Sài Gòn / hi: text Missing, audio Missing; Chưa chạy/xử lý tiếp theo checkpoint.

Lỗi dừng gần nhất:

```json
{
  "language": "vi",
  "status": "PausedQuota",
  "textReady": true,
  "audioReady": false,
  "errorCode": "QuotaExceeded",
  "audioDurationSeconds": null,
  "failureDetails": {
    "model": "gemini-3.1-flash-tts-preview",
    "httpStatus": 429,
    "finishReason": null,
    "quotaKind": "DailyOrZeroLimit",
    "retryAfterSeconds": 3600
  },
  "storageFailureDetails": null
}
```

429 ở model TTS, quotaKind DailyOrZeroLimit. retryAfterSeconds=3600 trong chẩn đoán là thời gian chờ được backend giới hạn/ghi nhận; không phải cam kết quota ngày sẽ reset sau một giờ. Cần kiểm tra quota thực tế trên AI Studio của đúng project/model trước khi chạy tiếp. Không tự lặp generation, đổi model/key/tài khoản hoặc bật billing.

## Mẫu nghe và văn bản

Văn bản đối chiếu đầy đủ trong artifacts/content-batch-20261023/matrix-final.json. Các kiểm tra script/ngôn ngữ và không có lời dẫn chỉ là kiểm tra kỹ thuật; chưa thẩm định chất lượng bản dịch/giọng đọc bằng người biết ngôn ngữ.

- Nhà thờ Đức Bà / vi: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/vi/a091bc7c5029df494de2ac60dbac63d676bb438ff1851b06ac5787e7383f7496.wav), 32.92s, 1,580,204 byte, audio/wav.
- Nhà thờ Đức Bà / en: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/en/2390eb6b7322243b986afd07caa45a45935c1c0225f5b14e059b70234138a207.wav), 37.64s, 1,806,764 byte, audio/wav.
- Nhà thờ Đức Bà / zh-hans: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/zh-hans/5f063aaa02071ea7f45a41518a3f812c62de1d32274c9f72754fe2be6ca3424d.wav), 33.68s, 1,616,684 byte, audio/wav.
- Nhà thờ Đức Bà / ja: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ja/fe8928e74157b5712003c57bfa6238e2181d0cdf0e73486428cb4e04f6fdb0f4.wav), 40.60s, 1,948,844 byte, audio/wav.
- Nhà thờ Đức Bà / ko: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ko/f5182a7e0966b7962ca47a46f1d147419445e782b8f2df36ecd011b14ccaa1c5.wav), 40.20s, 1,929,644 byte, audio/wav.
- Nhà thờ Đức Bà / fr: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/fr/e2ed3cd6a870457c6f3117a3df7b975210ef4df40af4c79ec456536034916ecd.wav), 36.68s, 1,760,684 byte, audio/wav.
- Nhà thờ Đức Bà / de: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/de/90c9e36efe67f122172f122fbcef8a25bc0de2972dcb7d3d3ec090b46fdc3b64.wav), 39.32s, 1,887,404 byte, audio/wav.
- Nhà thờ Đức Bà / es: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/es/f678a38f66d183b1fd92bc67a8e15314fd561f843c6d4e45a82b6db665aefe6f.wav), 40.48s, 1,943,084 byte, audio/wav.
- Nhà thờ Đức Bà / ru: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ru/ead043cd5451d5b80bca1595df9544ac6cb4e9eea8a88c25c98a887c58c4c450.wav), 37.76s, 1,812,524 byte, audio/wav.
- Nhà thờ Đức Bà / it: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/it/ac7983350bfb2217ee4ee3706d381a8aef56ef4e3fd930434b82760c7bcd9fdc.wav), 40.00s, 1,920,044 byte, audio/wav.
- Nhà thờ Đức Bà / pt: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/pt/66e2db65c5920345e7787d187718d0a663af297477f2a2ce62404037b29e6f6e.wav), 40.72s, 1,954,604 byte, audio/wav.
- Nhà thờ Đức Bà / th: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/th/7273025f01624da17df02664c4b17e4c4eec2d808bdc74b618c0f94509f4e153.wav), 43.88s, 2,106,284 byte, audio/wav.
- Nhà thờ Đức Bà / id: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/id/f1b51f02e6b1ddc19f0360175976b5883cc5c6a1b10570a875f9f7628c399947.wav), 39.00s, 1,872,044 byte, audio/wav.
- Nhà thờ Đức Bà / ms: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ms/a9813c64a6315b5b48c5e5d251913c0aec30d325cdba95fadecbdf43c9e8860a.wav), 38.60s, 1,852,844 byte, audio/wav.
- Nhà thờ Đức Bà / hi: [Nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/hi/ab55e1942c672469783d1ea3ab1071f61cbf53e73f425b7afd879f9909de137a.wav), 35.16s, 1,687,724 byte, audio/wav.

Bằng chứng tiến độ: progress.json, sql-final.json, storage-final.json, matrix-final.json, batch-*.json trong artifacts/content-batch-20261023. Native browser/direct/curl/HTTP2 probe nằm trong artifacts/storage-network-probe. Không có key/mật khẩu/JWT trong báo cáo.
