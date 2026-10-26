# Kiểm chứng HTTP thật — nội dung theo vị trí/yêu cầu, giữ tạm dừng

Thời gian request: `2026-10-24T16:51:30Z` đến `2026-10-24T16:56:43Z` (UTC).

Kết quả: các luồng đọc, Ready, GenerationPaused, nearby hỗn hợp, validation, rate limit và đọc WAV public đạt hợp đồng đã ghi. Có thể bàn giao backend cho frontend tích hợp nội dung hiện có và trạng thái tạm dừng. Chưa xác nhận khả năng sinh nội dung thật hoặc quota Gemini đã phục hồi.

## Kết nối và cấu hình hiệu lực

- Dùng Windows curl với `--noproxy "*" --ipv4`, timeout kết nối 3 giây, timeout HTTP API 10 giây; giữ kiểm tra chứng chỉ TLS. Không dùng JWT/key hoặc cookie trong request kiểm chứng.
- HTTP 5133 trả 307 với Location sang HTTPS 7239; HTTPS settings trả 200, đúng 500 m / 50 m / 3 giây / polling 3 giây. Lỗi kết nối ở lượt trước không được dùng làm bằng chứng API đã dừng.
- Giữ nguyên PID **15560**, executable `E:\AutoDubbingSystem\AutoDubbing.API\bin\Debug\net10.0\AutoDubbing.API.exe`, ContentRoot `E:\AutoDubbingSystem\AutoDubbing.API`, môi trường `Development`. Không restart hoặc dừng ứng dụng.
- Tên đầy đủ: **`ContentPreparation:AcceptNewGeneration=false`**; tên biến môi trường tương ứng `ContentPreparation__AcceptNewGeneration`.
- Đã đọc chọn lọc môi trường/tham số của chính PID API và tái dựng thứ tự cấu hình: appsettings → appsettings.Development → User Secrets → môi trường tiến trình → tham số chạy. Nguồn thắng là [appsettings.json](../AutoDubbing.API/appsettings.json). Không có ghi đè cờ này từ các nguồn sau; file cấu hình không đổi sau lúc tiến trình khởi động. Xác minh lại sau lượt HTTP vẫn false, cùng PID. Báo cáo không chứa command line đầy đủ, connection string, key hoặc JWT.

## Các trường hợp thực tế

| Request / trường hợp | HTTP | Kết quả quan sát |
| --- | --- | --- |
| GET `/api/languages` | 200 | Đúng 15 mã LanguageCatalog |
| GET `/api/pois`, GET `/api/pois/1` | 200 | Đúng 8 Published từ SQL; mô tả nguồn Nhà thờ khớp SQL |
| GET `/api/pois/1/content?language={mã}` với cả 15 mã | 200 × 15 | Text không rỗng, actualLanguage đúng yêu cầu, isFallback=false, audio đúng mã và URL SQL |
| GET `/api/pois/2/content?language=en` | 200 | Fallback vi, isFallback=true, audio=null; không tính là bản dịch en thành công |
| POST `/api/pois/1/prepare-content` body `{"language":"vi"}` | 200 | Ready, textReady/audioReady=true, jobId/statusUrl=null; 0.203397 giây |
| POST `/api/pois/1/prepare-content` body `{"language":"en"}` | 200 | Ready đúng en, không fallback, jobId/statusUrl=null; 0.034409 giây |
| POST `/api/pois/2/prepare-content` với en và vi | 503 × 2 | Unavailable / GenerationPaused; không có jobId/statusUrl. en textReady=false; vi textReady=true do dùng mô tả gốc; audioReady=false |
| POST `/api/pois/prepare-nearby` tại (10.7798,106.699) | 200 | POI 1 Ready ở 0 m, rồi POI 2 Unavailable/GenerationPaused ở 100.793401 m |
| POST `/api/pois/prepare-nearby` tại (10.78,106.6999) | 200 | POI 2 Unavailable ở 0 m, rồi POI 1 Ready ở 100.793401 m |
| POST nearby tại (0,0) | 200 | items=[] |
| POST prepare-content: xx, en-US, khoảng trắng ` en`, thiếu language | 400 × 4 | Validation trước admission; không tiêu 8 permit hợp lệ của client |
| POST nearby: thiếu tọa độ, latitude=91, longitude=181, latitude="NaN" | 400 × 4 | Input không hợp lệ bị từ chối |
| GET và POST POI 2147483647 | 404 × 2 | Không có POI; không tạo job |
| GET job UUID ngẫu nhiên không tồn tại | 404 | Không có job; GET không tạo công việc |
| POST hợp lệ thứ 9 từ cùng client | 429 | code=RequestRateLimited, Retry-After=59, Cache-Control=no-store |
| GET settings khi client bị giới hạn POST | 200 | Đọc không tiêu permit tạo nội dung |
| POST Ready sau khi chờ hết cửa sổ client | 200 | Ready; phục hồi sau thời gian Retry-After |
| POST thứ 31 toàn instance, client này mới được nhận 6 request | 429 | Giới hạn global 30/phút hoạt động độc lập client 8/phút; Retry-After=56 |
| POST Ready sau khi chờ hết cửa sổ global | 200 | Ready, jobId=null; không restart để reset limiter |
| GET WAV public, không JWT/key | 200 | audio/wav, 1,580,204 byte, 32.92 giây, mono PCM 16-bit / 24000 Hz |

Nearby đã được so sánh độc lập bằng Haversine từ toàn bộ tọa độ SQL: bán kính 500 m, thứ tự distance rồi ID, sai lệch nhỏ hơn 0.00001 m cho các mục trả về. Mỗi item chỉ có ngôn ngữ nguồn vi, không job mới. Live hiện có 8 Published và **0 Hidden**; loại Hidden khi thật sự có bản ghi Hidden vẫn chỉ được chứng minh bằng test mock, không đổi POI để tạo ca thử.

Kiểm tra global dùng các source address loopback 127.0.0.1–127.0.0.4 bằng bind trên từng curl request. Số permit trước khi bị global chặn lần lượt 8+8+8+6=30. Không sửa NIC, firewall, DNS, proxy hoặc trusted-forwarded-header để tạo client giả. Cả hai cửa sổ đều được chờ qua Retry-After cộng khoảng đệm trước bước phục hồi.

**Khác biệt so với hợp đồng:** không phát hiện khác biệt hành vi trong các ca trên. Nearby có Ready và thiếu cùng trả 200, trạng thái từng item khác nhau; HTTP 200 không có nghĩa tất cả audio đã Ready. GET POI đầu tiên mất 9.346964 giây; các POST Ready sau đó khoảng 20–345 ms ở lượt này. Đây là quan sát một lượt local, chưa xác định nguyên nhân độ trễ đầu và chưa phải kiểm thử tải.

## Đối chiếu dữ liệu

| Dữ liệu | Trước | Sau |
| --- | ---: | ---: |
| Admin | 1 | 1 |
| POI | 8 | 8 |
| Bản dịch | 14 | 14 |
| Bản dịch Ready | 14 | 14 |
| Audio | 15 | 15 |
| Audio Ready | 15 | 15 |
| Session | 0 | 0 |
| Route | 0 | 0 |
| Usage event | 0 | 0 |
| System log | 0 | 0 |
| File cache ở hai thư mục cache đã kiểm kê | 0 | 0 |

- Ngoài số lượng, toàn bộ snapshot POI/mô tả/trạng thái/tọa độ/bán kính, bản dịch/id/text/UpdatedAt và audio/id/URL/duration/status trước/sau bằng nhau. Cache manifest bằng nhau.
- Các response chuẩn bị đều không có jobId/statusUrl và không có 202. Từ nhánh Ready và công tắc false trong [service](../AutoDubbing.Service/ContentPreparation/ContentPreparationService.cs), các request trên không đi vào queue/provider. Không gửi request Gemini/Admin generation trong kịch bản.
- Không thu EventPipe/trace hoặc bộ đếm outbound của PID API; vì vậy không đưa ra số đo runtime “0 call provider”. Bằng chứng lượt này là nguồn cấu hình hiệu lực, response Ready/Paused không job, snapshot không đổi và đối chiếu nhánh source; các assert 0 provider call trực tiếp nằm trong test mock đã bàn giao trước.
- Storage chỉ có GET một object public có sẵn. Không upload/xóa/ghi file Supabase. Không kiểm kê lại toàn bộ bucket hoặc khẳng định tổng số object bucket hôm nay; 15 audio Ready ở đây là số hàng SQL.

[Mở WAV tiếng Việt Nhà thờ Đức Bà](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/vi/a091bc7c5029df494de2ac60dbac63d676bb438ff1851b06ac5787e7383f7496.wav). SHA256 thực tế: `a8abceaef8f31f7b641007fc369494d36b21573c5d98d30714232db7d1bd8e63`. Đọc WAV và HTTP 200 là kiểm tra kỹ thuật, chưa phải thẩm định giọng đọc/bản dịch.

## Phần còn cần kiểm chứng

- Chỉ mock: nhận 202 khi bật sinh mới, vòng đời queued/processing, full queue, ưu tiên/chống trùng đồng thời, source đổi khi đang chạy, provider retry/quota và ghi Ready sau upload/SQL. Không bật sinh mới để thử các ca này trong đợt HTTP.
- Cần quota/quyền Gemini thực tế để xác minh luồng dịch/TTS mới. Quota chưa được kiểm tra bằng request Gemini; không thay model/key hoặc billing.
- Cần frontend/thiết bị: GPS tin cậy, dwell 50 m liên tục 3 giây, hủy khi rời vùng/mất fix, audio về muộn/đổi ngôn ngữ, nhiều POI nhưng một audio, một lần autoplay/POI/visit, phát lại thủ công và autoplay bị trình duyệt chặn.
- Giữ giới hạn một instance: queue/jobId/rate counters/cooldown mất khi restart; SQL/cache/object đã lưu vẫn giữ. Bản dịch lịch sử chưa có source revision trong schema; cần quản lý invalidation khi sau này cho sửa nguồn.

Không sửa source ứng dụng hoặc schema, không thêm frontend, không commit/push/merge. Chỉ bổ sung công cụ/bằng chứng trong artifacts và báo cáo này. Không chạy lại suite vì không sửa source; kết quả 246/246 mock trước đó vẫn là kết quả của đợt triển khai, không gắn nhãn kiểm thử Gemini thật.

## Bằng chứng và toàn bộ request

Tổng **77 request**, **0 lỗi transport**. Phân bố HTTP: `{"200": 61, "307": 1, "400": 8, "404": 3, "429": 2, "503": 2}`. TLS được giữ nguyên; không gửi credential header.

- [Cấu hình runtime đã lọc](../artifacts/on-demand-http-20261024/runtime-proof.json)
- [Toàn bộ request/response JSON](../artifacts/on-demand-http-20261024/http-results.json)
- [SQL trước](../artifacts/on-demand-http-20261024/sql-before.json), [SQL sau](../artifacts/on-demand-http-20261024/sql-after.json), [so sánh](../artifacts/on-demand-http-20261024/comparison.json)
- [WAV proof](../artifacts/on-demand-http-20261024/audio-proof.json), [global rate proof](../artifacts/on-demand-http-20261024/global-limit.json)

| # | UTC | Method/path | Body | HTTP | Giây | Retry-After |
| --- | --- | --- | --- | ---: | ---: | --- |
| 1 | 2026-10-24T16:51:30Z | `GET /api/content-preparations/settings` | `—` | 307 | 0.003107 | — |
| 2 | 2026-10-24T16:51:30Z | `GET /api/content-preparations/settings` | `—` | 200 | 0.038052 | — |
| 3 | 2026-10-24T16:51:30Z | `GET /api/languages` | `—` | 200 | 0.102343 | — |
| 4 | 2026-10-24T16:51:40Z | `GET /api/pois` | `—` | 200 | 9.346964 | — |
| 5 | 2026-10-24T16:51:41Z | `GET /api/pois/1` | `—` | 200 | 0.567180 | — |
| 6 | 2026-10-24T16:51:41Z | `GET /api/pois/1/content?language=vi` | `—` | 200 | 0.396731 | — |
| 7 | 2026-10-24T16:51:41Z | `GET /api/pois/1/content?language=en` | `—` | 200 | 0.149768 | — |
| 8 | 2026-10-24T16:51:41Z | `GET /api/pois/1/content?language=zh-hans` | `—` | 200 | 0.037506 | — |
| 9 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=ja` | `—` | 200 | 0.052203 | — |
| 10 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=ko` | `—` | 200 | 0.024443 | — |
| 11 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=fr` | `—` | 200 | 0.022647 | — |
| 12 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=de` | `—` | 200 | 0.019657 | — |
| 13 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=es` | `—` | 200 | 0.058781 | — |
| 14 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=ru` | `—` | 200 | 0.031760 | — |
| 15 | 2026-10-24T16:51:42Z | `GET /api/pois/1/content?language=it` | `—` | 200 | 0.024359 | — |
| 16 | 2026-10-24T16:51:43Z | `GET /api/pois/1/content?language=pt` | `—` | 200 | 0.137854 | — |
| 17 | 2026-10-24T16:51:43Z | `GET /api/pois/1/content?language=th` | `—` | 200 | 0.031173 | — |
| 18 | 2026-10-24T16:51:43Z | `GET /api/pois/1/content?language=id` | `—` | 200 | 0.034508 | — |
| 19 | 2026-10-24T16:51:43Z | `GET /api/pois/1/content?language=ms` | `—` | 200 | 0.019071 | — |
| 20 | 2026-10-24T16:51:43Z | `GET /api/pois/1/content?language=hi` | `—` | 200 | 0.020335 | — |
| 21 | 2026-10-24T16:51:43Z | `GET /api/pois/2/content?language=en` | `—` | 200 | 0.023764 | — |
| 22 | 2026-10-24T16:51:43Z | `GET /api/pois/2147483647` | `—` | 404 | 0.110135 | — |
| 23 | 2026-10-24T16:51:44Z | `GET /api/content-preparations/51eac7d3-5830-45b4-aafa-b46e0e4d87f4` | `—` | 404 | 0.052551 | — |
| 24 | 2026-10-24T16:52:56Z | `GET WAV public (URL ở trên)` | `—` | 200 | 1.817553 | — |
| 25 | 2026-10-24T16:53:10Z | `POST /api/pois/1/prepare-content` | `{"language":"xx"}` | 400 | 0.077423 | — |
| 26 | 2026-10-24T16:53:10Z | `POST /api/pois/1/prepare-content` | `{"language":"en-US"}` | 400 | 0.014779 | — |
| 27 | 2026-10-24T16:53:10Z | `POST /api/pois/1/prepare-content` | `{"language":" en"}` | 400 | 0.016678 | — |
| 28 | 2026-10-24T16:53:11Z | `POST /api/pois/1/prepare-content` | `{}` | 400 | 0.077314 | — |
| 29 | 2026-10-24T16:53:11Z | `POST /api/pois/prepare-nearby` | `{}` | 400 | 0.049788 | — |
| 30 | 2026-10-24T16:53:11Z | `POST /api/pois/prepare-nearby` | `{"latitude":91,"longitude":0}` | 400 | 0.042075 | — |
| 31 | 2026-10-24T16:53:11Z | `POST /api/pois/prepare-nearby` | `{"latitude":0,"longitude":181}` | 400 | 0.045022 | — |
| 32 | 2026-10-24T16:53:11Z | `POST /api/pois/prepare-nearby` | `{"latitude":"NaN","longitude":0}` | 400 | 0.020317 | — |
| 33 | 2026-10-24T16:53:12Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.203397 | — |
| 34 | 2026-10-24T16:53:12Z | `POST /api/pois/1/prepare-content` | `{"language":"en"}` | 200 | 0.034409 | — |
| 35 | 2026-10-24T16:53:12Z | `POST /api/pois/2/prepare-content` | `{"language":"en"}` | 503 | 0.027096 | — |
| 36 | 2026-10-24T16:53:12Z | `POST /api/pois/2/prepare-content` | `{"language":"vi"}` | 503 | 0.019611 | — |
| 37 | 2026-10-24T16:53:12Z | `POST /api/pois/prepare-nearby` | `{"latitude":10.7798,"longitude":106.699}` | 200 | 0.090771 | — |
| 38 | 2026-10-24T16:53:12Z | `POST /api/pois/prepare-nearby` | `{"latitude":10.78,"longitude":106.6999}` | 200 | 0.032766 | — |
| 39 | 2026-10-24T16:53:12Z | `POST /api/pois/2147483647/prepare-content` | `{"language":"en"}` | 404 | 0.044509 | — |
| 40 | 2026-10-24T16:53:13Z | `POST /api/pois/prepare-nearby` | `{"latitude":0,"longitude":0}` | 200 | 0.021566 | — |
| 41 | 2026-10-24T16:53:13Z | `POST /api/pois/1/prepare-content` | `{"language":"en"}` | 429 | 0.042325 | 59 |
| 42 | 2026-10-24T16:53:13Z | `GET /api/content-preparations/settings` | `—` | 200 | 0.014598 | — |
| 43 | 2026-10-24T16:55:03Z | `POST /api/pois/1/prepare-content` | `{"language":"en"}` | 200 | 0.189873 | — |
| 44 | 2026-10-24T16:55:03Z | `GET /api/content-preparations/settings` [src 127.0.0.2] | `—` | 200 | 0.016232 | — |
| 45 | 2026-10-24T16:55:03Z | `GET /api/content-preparations/settings` [src 127.0.0.3] | `—` | 200 | 0.021565 | — |
| 46 | 2026-10-24T16:55:04Z | `GET /api/content-preparations/settings` [src 127.0.0.4] | `—` | 200 | 0.019399 | — |
| 47 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.020736 | — |
| 48 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.026880 | — |
| 49 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.021946 | — |
| 50 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.036012 | — |
| 51 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.024895 | — |
| 52 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.021558 | — |
| 53 | 2026-10-24T16:55:04Z | `POST /api/pois/1/prepare-content` | `{"language":"vi"}` | 200 | 0.021327 | — |
| 54 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.021638 | — |
| 55 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.023347 | — |
| 56 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.025164 | — |
| 57 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.024579 | — |
| 58 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.024120 | — |
| 59 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.046093 | — |
| 60 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.043441 | — |
| 61 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.2] | `{"language":"vi"}` | 200 | 0.023533 | — |
| 62 | 2026-10-24T16:55:05Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.024780 | — |
| 63 | 2026-10-24T16:55:06Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.021375 | — |
| 64 | 2026-10-24T16:55:06Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.172799 | — |
| 65 | 2026-10-24T16:55:06Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.025358 | — |
| 66 | 2026-10-24T16:55:06Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.021400 | — |
| 67 | 2026-10-24T16:55:06Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.022101 | — |
| 68 | 2026-10-24T16:55:06Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.045639 | — |
| 69 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.3] | `{"language":"vi"}` | 200 | 0.020756 | — |
| 70 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 200 | 0.022541 | — |
| 71 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 200 | 0.021244 | — |
| 72 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 200 | 0.030231 | — |
| 73 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 200 | 0.021732 | — |
| 74 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 200 | 0.023685 | — |
| 75 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 200 | 0.026197 | — |
| 76 | 2026-10-24T16:55:07Z | `POST /api/pois/1/prepare-content` [src 127.0.0.4] | `{"language":"vi"}` | 429 | 0.021392 | 56 |
| 77 | 2026-10-24T16:56:43Z | `POST /api/pois/1/prepare-content` | `{"language":"en"}` | 200 | 0.344584 | — |
