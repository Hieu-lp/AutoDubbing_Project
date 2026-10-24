# Kiểm chứng pipeline thật — Nhà thờ Đức Bà

> Báo cáo lịch sử ngày 20/10, không phải kiểm kê hiện hành. Kết quả sau đó xem [Storage trực tiếp 23/10](storage-direct-20261023.md). Từ 24/10 hủy kế hoạch batch 120, chuyển sang [nội dung theo vị trí/yêu cầu](content-on-demand.md); giữ nguyên mọi kết quả đã lưu.

Ngày 20/10/2026, nhánh backend-api. Phạm vi được cho phép: đúng POI Nhà thờ Đức Bà hiện có; ID 1 được tra cứu từ SQL, không mặc định từ tên. POI vẫn Published, mô tả gốc vi không đổi.

**Kết quả: 4/15 ngôn ngữ hoàn tất kỹ thuật cả văn bản và audio.** Tiếng Trung giản thể đã có bản dịch Ready nhưng TTS lỗi GenerationBlockedOrIncomplete. Dừng mở rộng; 10 ngôn ngữ tiếp theo chưa gọi. Không kết luận lỗi này là hết quota hoặc chặn nội dung vì response chi tiết không được lưu.

## Ma trận đủ 15 ngôn ngữ

| Mã | Ngôn ngữ | Văn bản SQL | Audio SQL | URL ẩn danh | Giây WAV / SQL | Lỗi hoặc phần còn lại |
| --- | --- | --- | --- | --- | --- | --- |
| vi | Việt | Gốc vi | Ready | [Mở nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/vi/a091bc7c5029df494de2ac60dbac63d676bb438ff1851b06ac5787e7383f7496.wav) — HTTP 200 | 32.92 / 33 | — |
| en | Anh Mỹ | Ready | Ready | [Mở nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/en/2390eb6b7322243b986afd07caa45a45935c1c0225f5b14e059b70234138a207.wav) — HTTP 200 | 37.64 / 38 | — |
| zh-hans | Trung giản thể | Ready | Missing | Chưa có | — | GenerationBlockedOrIncomplete |
| ja | Nhật | Ready | Ready | [Mở nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ja/fe8928e74157b5712003c57bfa6238e2181d0cdf0e73486428cb4e04f6fdb0f4.wav) — HTTP 200 | 40.60 / 41 | — |
| ko | Hàn | Ready | Ready | [Mở nghe](https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ko/f5182a7e0966b7962ca47a46f1d147419445e782b8f2df36ecd011b14ccaa1c5.wav) — HTTP 200 | 40.20 / 41 | — |
| fr | Pháp | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| de | Đức | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| es | Tây Ban Nha | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| ru | Nga | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| it | Ý | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| pt | Bồ Đào Nha Brazil | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| th | Thái | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| id | Indonesia | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| ms | Mã Lai | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |
| hi | Hindi | Missing | Missing | Chưa có | — | Chưa gọi do dừng lượt |

Bốn URL trên đã được GET không gửi apikey hoặc JWT: HTTP 200, Content-Type audio/wav. Đã so sánh URL API content với SQL, object Supabase, hash văn bản trong đường dẫn, dữ liệu WAV và thời lượng. ffprobe độc lập xác nhận PCM signed 16-bit little-endian, mono, 24.000 Hz; frame WAV/Python và ffprobe cùng thời lượng. SQL thoi_luong lưu số giây làm tròn lên.

**Chưa nghe thẩm định cả bốn audio.** Ready và HTTP 200 là kết quả kỹ thuật; không chứng minh phát âm, đọc đúng mọi từ hoặc chất lượng dịch đã được duyệt bởi người biết ngôn ngữ.

Model gọi thật thành công: gemini-3.1-flash-lite cho en/zh-hans/ja/ko; gemini-3.1-flash-tts-preview cho vi/en/ja/ko. Tiếng Việt dùng nguyên mô tả, không tạo BAN_DICH vi. Không đổi model hoặc bật billing.

API content trả actualLanguage khớp vi/en/zh-hans/ja/ko, isFallback=false; zh-hans có audio=null. Mười ngôn ngữ chưa sinh vẫn trả fallback vi với isFallback=true; không tính là bản dịch hoàn thành.

## Chứng minh gọi lại không tạo trùng

| Mốc | BAN_DICH của POI | AUDIO của POI | File Supabase của POI |
| --- | --- | --- | --- |
| Trước lần tạo vi/en đầu tiên | 0 | 0 | 0 |
| vi/en hoàn tất, trước gọi lại | 1 | 2 | 2 |
| Sau gọi lại vi/en lượt đầu | 1 | 2 | 2 |
| Sau resume và gọi lại vi/en | 1 | 2 | 2 |
| Sau nhóm zh-hans/ja/ko | 4 | 4 | 4 |

Hai lần kiểm chứng gọi lại vi/en đều trả SkippedReady. Không chỉ số lượng: toàn bộ snapshot hàng SQL và ID/metadata object Supabase trước/sau đều giống nhau. Không gọi Gemini hoặc upload lại cho phần Ready. Snapshot cuối không có hai bản ghi cùng ngôn ngữ, không có file thừa trong prefix của POI. Chưa chạy kiểm chứng gọi lại cho ja/ko hoặc cả 15 vì đã dừng ở lỗi TTS zh-hans; không tuyên bố bằng chứng gọi lại toàn danh mục.

## Sửa tối thiểu và kiểm tra

- Sửa GeminiContentProvider.cs: điều kiện giữ nguyên năm dùng ranh giới chữ số thay cho ranh giới từ Unicode. Dạng 1880年/1880년에 hợp lệ trước đó bị từ chối; 18800 hoặc 1881 vẫn bị từ chối. Giữ kiểm tra tên POI và các điều kiện an toàn khác.
- Thêm 6 ca mock hồi quy trong GeminiProviderTests.cs. Trước sửa: 3 fail/3 pass; sau sửa: toàn bộ 195/195 PASS, không bỏ qua.
- Build solution cuối: 0 cảnh báo, 0 lỗi. Test dùng mock, không gọi provider thật hoặc ghi AutoDubbingDb.

~~~powershell
dotnet build AutoDubbingSystem.slnx --no-restore -p:UseAppHost=false -p:BaseOutputPath=E:\AutoDubbingSystem\artifacts\real-pipeline-build-v2\
dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-restore -p:UseAppHost=false -p:BaseOutputPath=E:\AutoDubbingSystem\artifacts\real-pipeline-build-v2\ --logger trx --results-directory artifacts/test-results/multilingual-live-v2
~~~

Supabase dùng bucket public đã cấu hình, không tạo bucket hoặc đổi quyền. Key sb_secret gửi bằng apikey; không dùng nó làm Bearer JWT. Giá trị Gemini, Supabase và JWT signing key chỉ đọc nội bộ từ cấu hình, không ghi trong command line, báo cáo hoặc source. Mật khẩu Hiu nhập ẩn trong terminal, JWT chỉ giữ trong bộ nhớ; login và /me đều HTTP 200.

Chỉ restart đúng API workspace để nạp code đã kiểm tra; API cuối PID 18448 dùng bản real-pipeline-build-v2, HTTPS localhost:7239 và HTTP localhost:5133. Không dừng tiến trình dotnet khác. Lượt tương tác resume kết thúc trước khi lưu snapshot cuối; bước đối chiếu bổ sung chỉ đọc SQL/storage/public API, không đăng nhập hoặc sinh thêm nội dung.

## Checkpoint và giới hạn còn lại

- Giữ 4 BAN_DICH Ready (en, zh-hans, ja, ko), 4 AUDIO Ready và 4 object công khai (vi, en, ja, ko). Lượt tiếp theo cho zh-hans chỉ cần TTS/upload; không dịch lại phần Ready.
- Cần xác định nguyên nhân response TTS zh-hans bị đánh dấu GenerationBlockedOrIncomplete trước khi chủ động thử lại. Không tự retry lỗi này, đổi model, dịch qua ngôn ngữ khác hoặc chuyển trả phí.
- Chưa gọi fr/de/es/ru/it/pt/th/id/ms/hi. Khi tiếp tục vẫn tối đa 3 ngôn ngữ mới mỗi lượt; hiện chưa có tác vụ nền tự chạy tiếp.
- Đối chiếu cuối giữ nguyên cả 8 POI và bản ghi dịch/audio của 7 POI ngoài phạm vi. Không tạo POI, tài khoản, migration, upload Supabase ngoài prefix mục tiêu hoặc sửa schema. Không commit/push/merge.

## File và bằng chứng

Source thay đổi trong lượt kiểm chứng thật: AutoDubbing.Service/ContentGeneration/GeminiContentProvider.cs và AutoDubbing.Tests/GeminiProviderTests.cs. Tài liệu: multilingual-content.md, backend-demo.md và báo cáo này. Các thay đổi chưa commit có sẵn được giữ nguyên.

Công cụ và kết quả tạm nằm trong artifacts/multilingual-live-test/, được Git bỏ qua. runner.py dùng endpoint Admin được bảo vệ, finalize-report.py chỉ đối chiếu đọc sau khi runner kết thúc; không chạy tự động trong CI.

- [Báo cáo máy đọc được](../artifacts/multilingual-live-test/20261019T233253Z/report.json)
- [SQL cuối](../artifacts/multilingual-live-test/20261019T233253Z/sql-final-verification.json)
- [Object cuối](../artifacts/multilingual-live-test/20261019T233253Z/storage-final-verification.json)
- [Bằng chứng gọi lại vi/en](../artifacts/multilingual-live-test/20261019T233253Z/repeat-vi-en-proof.json)
- [ffprobe](../artifacts/multilingual-live-test/20261019T233253Z/audio-format-verification.json)
- Audio tải về: artifacts/multilingual-live-test/20261019T233253Z/audio/vi.wav, en.wav, ja.wav, ko.wav.
- Test: artifacts/test-results/multilingual-live-v2/DELL_DESKTOP-1MMSBHT_2026-10-20_06_29_41_net10.0.trx.

Nguồn cấu hình: [Supabase API keys](https://supabase.com/docs/guides/getting-started/api-keys), [public download](https://supabase.com/docs/guides/storage/serving/downloads).
