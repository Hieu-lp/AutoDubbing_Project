# Thử Gemini thủ công

Tài liệu này ghi lại công cụ và kết quả smoke test vi/en trước khi triển khai luồng sinh nội dung. Phạm vi sản phẩm là 15 ngôn ngữ; danh mục, API theo đợt và trạng thái kiểm chứng từng ngôn ngữ được mô tả trong [multilingual-content.md](multilingual-content.md). Kết quả gọi thật ở đây không chứng minh pipeline hoặc 13 ngôn ngữ khác đã chạy thật.

Công cụ này chỉ kiểm chứng dịch thuật và TTS, chạy riêng khỏi API. Không có endpoint mới, pipeline, ghi SQL, migration hoặc upload. Python 3.11 trở lên và Windows PowerShell có sẵn là đủ, không cài package.

## Cấu hình và chạy

Lưu `Gemini:ApiKey` trong User Secrets của `AutoDubbing.API` (UserSecretsId lấy từ csproj). Công cụ đọc trực tiếp file User Secrets cục bộ, hỗ trợ tên cấu hình phẳng hoặc JSON lồng nhau. Không truyền giá trị key qua tham số lệnh, không đưa vào URL, source hoặc log. Không dùng `dotnet user-secrets list` để thu thập log.

Từ thư mục gốc workspace:

```powershell
python -m unittest discover -s scripts -p test_gemini_smoke_test.py -v
python scripts/gemini_smoke_test.py
```

Lệnh thứ hai gọi dịch vụ thật và tiêu thụ quota của project. Công cụ không bật billing hoặc tự chuyển model. Tài khoản Windows chạy công cụ cần quyền SELECT trên SQL Server `.` / `AutoDubbingDb`. Công cụ dùng Windows Authentication, kết nối mã hóa và chấp nhận certificate SQL Server cục bộ. Cấu hình này phục vụ thử nghiệm trên máy hiện tại.

## Dữ liệu và kết quả

Chỉ truy vấn `dbo.POI` theo tên `Nhà thờ Đức Bà` bằng tham số SQL. Phải tìm thấy đúng một POI có tên khớp và ngôn ngữ gốc `vi`; nếu không, công cụ dừng. Lấy nguyên văn tối đa hai câu đầu, không sửa nội dung database. Prompt giữ nguyên tên riêng, mốc năm và tên phong cách kiến trúc trong đoạn gốc; lỗi tên riêng hoặc năm cần được người chạy xem lại, không tự sửa bản dịch bằng code.

Hai model cố định:

- Dịch: `gemini-3.1-flash-lite`.
- TTS: `gemini-3.1-flash-tts-preview`, giọng `Kore` cho cả hai ngôn ngữ.

Metadata model được kiểm tra trước, sau đó mới gọi `generateContent`. Metadata thành công không chứng minh có quota sinh nội dung. Mỗi lượt chạy dùng một thư mục thời gian UTC riêng trong `artifacts/gemini-smoke-test/`, đã được Git bỏ qua qua quy tắc `artifacts/`:

- `poi-source.json`, `source-vi.txt`: mô tả đọc từ SQL và đoạn gốc.
- `translation-en.txt`: bản dịch trả về từ Gemini, được dùng nguyên văn cho TTS tiếng Anh.
- `audio-vi.wav`, `audio-en.wav`: kết quả audio hợp lệ khi từng bước thành công.
- `report.json`: trạng thái từng bước, MIME từ provider, encoding, kích thước và thời lượng đo từ số frame thực tế.

Không dùng kết quả của thư mục cũ để kết luận cho lượt chạy mới. Không có file audio nghĩa là bước đó chưa thành công, không tạo file giả để thay thế.

Với MIME PCM có `rate` hợp lệ, đóng gói WAV RIFF PCM signed 16-bit little-endian mono theo hợp đồng Gemini TTS 3.1; dùng sample rate từ metadata thật. WAV có sẵn được kiểm tra rồi giữ nguyên container. Không đổi đuôi PCM thành MP3. Audio trống, thiếu sample rate, dữ liệu lệch frame, WAV bị cắt hoặc MIME chưa hỗ trợ đều bị từ chối. Thời lượng kỹ thuật không chứng minh đọc đủ và đúng văn bản; cần mở nghe hai file để đánh giá phát âm, tên riêng và khả năng đọc đúng transcript.

## Giới hạn và xử lý lỗi

HTTP timeout 55 giây mỗi thao tác mạng; không tự retry. HTTP 401, 403 và 429 dừng lượt chạy. HTTP 404 được ghi là model không khả dụng; không tự dùng model khác. Các lỗi chỉ ghi mã an toàn, bỏ URL/header/body lỗi và traceback có thể chứa thông tin nhạy cảm. Request không cho phép redirect nhằm tránh chuyển key sang host khác. Nếu dịch thất bại, không tạo audio tiếng Anh từ một bản dịch tự đặt.

Các kiểm thử offline mô phỏng HTTP 401/403/404/429 và timeout; kiểm tra đóng gói PCM, thời lượng, WAV bị cắt, không bọc WAV hai lần và giữ nguyên đoạn gốc. Chúng không gọi Gemini hoặc SQL và không được nối vào CI của backend.

Tài liệu Google liệt kê free tier cho hai model yêu cầu, nhưng quota và tier thực tế thuộc project. Công cụ không xác định được billing tier hay bảo đảm chi phí bằng 0. Nếu model không khả dụng, có thể xem `gemini-2.5-flash-lite` cho dịch và `gemini-2.5-flash-preview-tts` cho TTS trong bảng giá; cần xác minh lại khả năng truy cập và được người dùng chốt trước khi đổi. Không tự thử fallback. TTS 3.1 là preview và có thể thay đổi.

## Kết quả thực tế ngày 20/10/2026, giờ Việt Nam

Lượt cuối: `artifacts/gemini-smoke-test/20261019T211253171953Z/` (tên thư mục dùng UTC). Cả hai model yêu cầu đã thành công ở bước sinh nội dung thực tế. Đã đọc POI ID 1 bằng SELECT, dịch hai câu đầu và tạo hai audio. Provider trả `audio/l16; rate=24000; channels=1`; WAV đóng gói PCM 16-bit mono 24 kHz. Tiếng Việt dài 13,08 giây, 627.884 byte; tiếng Anh dài 14,12 giây, 677.804 byte. Thời lượng đo từ frame audio, không ước lượng theo số từ. Chưa nghe và chưa xác nhận chất lượng phát âm/đọc đúng transcript.

Bản dịch đầu đã thay tên riêng bằng tên tiếng Anh. Sau khi làm rõ ràng buộc trong prompt, lượt kế tiếp gặp HTTP 503 từ model dịch. Lượt cuối thành công, giữ tên riêng gốc, năm 1880 và các tên kiến trúc Roman/Gothic. Không gặp 401/403/404/429 trong các lời gọi thật; những nhánh này được kiểm tra bằng mock HTTP. Các thư mục lượt trước được giữ trong artifacts để không đánh đồng kết quả cũ với lượt cuối.

Không thay đổi source API, User Secrets, database, migration hoặc tiến trình API nền trong lần thử này. Không bật billing hoặc đổi model. Kiểm thử offline chạy bằng unittest, không gọi mạng/SQL.

Xác minh cuối: 9/9 kiểm thử offline PASS; ffprobe xác nhận cả hai file là `pcm_s16le`, mono 24 kHz với thời lượng đúng như trên. SELECT lại cho thấy toàn bộ dữ liệu POI đọc được vẫn khớp snapshot, đoạn thử là tiền tố nguyên văn của mô tả. Quét các file mới và artifacts không thấy giá trị Gemini key. Tiến trình API PID 12796 vẫn chạy.

Nguồn chính thức:

- [Gemini 3.1 Flash-Lite](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite)
- [Gemini 3.1 Flash TTS Preview](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-tts-preview)
- [Giá và free tier](https://ai.google.dev/gemini-api/docs/pricing)
- [Models API](https://ai.google.dev/api/models)
- [Google Firebase: TTS 3.1 và PCM](https://firebase.google.com/docs/ai-logic/generate-speech)
- [GenerateContent TTS: định dạng audio](https://ai.google.dev/gemini-api/docs/generate-content/speech-generation)
