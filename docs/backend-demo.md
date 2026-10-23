# Backend cốt lõi cho demo du khách

Phạm vi gồm đọc POI, nội dung/bản dịch/audio đã có và xem trước thứ tự ghé POI. Các API dưới đây cho phép truy cập ẩn danh, trả DTO, không trả thông tin Admin. Phần đăng nhập hiện có được giữ; cách cấu hình JWT và tạo Admin đầu tiên nằm trong [admin-auth.md](admin-auth.md).

**Cập nhật ORS:** `POST /api/routes/preview` hiện trả tuyến đi bộ OpenRouteService `foot-walking`, kèm thời gian và geometry GeoJSON. Xem cấu hình key, hợp đồng response, lỗi và attribution trong [walking-routes.md](walking-routes.md). Nearby vẫn dùng Haversine. Các kết quả 39/39 HTTP và khoảng cách đường chim bay ghi ở cuối tài liệu thuộc phiên bản trước ORS, không phải xác nhận tuyến đi bộ thật.

**Cập nhật đa ngôn ngữ:** backend đã có danh mục 15 ngôn ngữ dùng chung và API Admin `POST /api/admin/pois/{id}/generate-content` xử lý theo đợt, giữ checkpoint dịch/TTS/upload. Xem [multilingual-content.md](multilingual-content.md) để cấu hình Supabase public, đọc trạng thái từng ngôn ngữ và giới hạn retry. Các API GET bên dưới vẫn chỉ đọc và fallback rõ ràng; không tự sinh nội dung. Đã kiểm chứng pipeline thật riêng Nhà thờ Đức Bà: vi/en/ja/ko hoàn tất kỹ thuật; zh-hans mới có bản dịch Ready, TTS lỗi và đã dừng trước 10 ngôn ngữ còn lại. Xem [báo cáo thực tế](multilingual-live-verification.md); chưa nghe thẩm định giọng đọc.

## Chạy local

Từ `E:\AutoDubbingSystem`, dùng .NET 10 SDK và SQL Server `.`, Windows Authentication, database `AutoDubbingDb`:

```powershell
dotnet restore AutoDubbingSystem.slnx
dotnet build AutoDubbingSystem.slnx --no-restore
dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore
dotnet run --project AutoDubbing.API --no-build --launch-profile https
```

Profile `https` dùng `https://localhost:7239` và `http://localhost:5133`, môi trường Development. HTTP chuyển hướng tới HTTPS khi chạy profile này. Cần có signing key trong User Secrets theo tài liệu đăng nhập, kể cả khi chỉ gọi API du khách. Không ghi key vào source, request mẫu hoặc Docker image. Nếu máy chưa tin cậy chứng chỉ phát triển, chạy `dotnet dev-certs https --trust` và xác nhận trên máy; không bỏ qua xác minh TLS.

OpenAPI trong Development: `https://localhost:7239/openapi/v1.json`. Không có Swagger UI được bổ sung trong đợt này.

API không tự chạy migration hay seed. Hai migration hiện có vẫn được giữ nguyên; các API mới không đổi schema và không cần migration mới. Với database mới, xem SQL và áp dụng migration theo tài liệu đăng nhập trước khi sử dụng.

## Quy tắc dữ liệu đã chốt

| Thành phần | Quy tắc phục vụ du khách |
| --- | --- |
| `POI.trang_thai` | Chỉ `Published`; `Hidden` không được trả. Trạng thái khác cũng không công khai. |
| `BAN_DICH.trang_thai`, `AUDIO.trang_thai` | Chỉ `Ready`; các trạng thái khác không được trả. |
| Nhiều bản dịch Ready cùng POI/ngôn ngữ | `UpdatedAt` mới nhất; nếu bằng nhau chọn ID lớn nhất. |
| Nhiều audio Ready cùng POI/ngôn ngữ | ID lớn nhất. |
| `AUDIO.duong_dan_file` | URL HTTPS đầy đủ; storage path hoặc URL không hợp lệ trả `audio: null`. |
| URL ảnh | Có thể `null`; không tạo URL giữ chỗ. |

So sánh trạng thái dùng `Latin1_General_100_BIN2` ngay trong truy vấn SQL để không phụ thuộc collation mặc định của database; hãy nhập đúng `Published`, `Hidden`, `Ready`. Không tự chuyển các trạng thái khác thành trạng thái hợp lệ.

Mã ngôn ngữ là tên culture không rỗng được .NET hỗ trợ, ví dụ `vi`, `en`, `en-US`. Input không phân biệt hoa/thường, được chuẩn hóa thành chữ thường trong response nội dung (`en-US` → `en-us`). Không chấp nhận khoảng trắng đầu/cuối, chuỗi tùy ý hoặc mã không được hỗ trợ. Trong database, dùng mã hợp lệ không kèm khoảng trắng cho ngôn ngữ gốc, bản dịch và audio. `en` và `en-us` là hai mã khác nhau; không tự tìm ngôn ngữ cha.

Nội dung:

1. Yêu cầu trùng ngôn ngữ gốc: trả `mo_ta_goc`, không tìm bản dịch.
2. Yêu cầu khác: tìm bản dịch `Ready` đúng mã ngôn ngữ theo thứ tự chọn ở trên.
3. Thiếu bản dịch: trả mô tả gốc, `actualLanguage` là ngôn ngữ gốc và `isFallback: true`.
4. Chỉ tìm audio `Ready` khớp **ngôn ngữ thực tế của văn bản**. Thiếu audio thì `null`.

Audio được chọn phải là URL tuyệt đối HTTPS có host và không chứa username/password trong URL. Nếu bản ghi được chọn có URL sai, trả `null`; không tự chọn bản cũ hơn. API không tải file, kiểm tra khả năng truy cập URL hoặc xác minh audio đọc đúng nội dung; người nhập dữ liệu chịu trách nhiệm file và quyền truy cập. Không gọi dịch máy/TTS và không tạo file âm thanh. Chưa đưa `Duration` vào response vì đơn vị trường này chưa được chốt.

## Các endpoint và response

| Request | Thành công | Lỗi đầu vào / không có dữ liệu |
| --- | --- | --- |
| `GET /api/pois` | `200`, mảng POI, tăng dần theo ID | Không có POI công khai: `200 []` |
| `GET /api/pois/{id:int}` | `200`, chi tiết một POI | Thiếu/ẩn: `404` |
| `GET /api/pois/nearby?latitude=...&longitude=...&radiusMeters=...` | `200`, mảng POI trong bán kính, gần nhất trước | Thiếu/sai tham số: `400`; không có kết quả: `200 []` |
| `GET /api/pois/{id:int}/content?language=vi` | `200`, văn bản và audio tùy dữ liệu | Thiếu/sai language: `400`; thiếu/ẩn POI: `404` |
| `POST /api/routes/preview` | `200`, các POI theo thứ tự ghé, khoảng cách đi bộ, thời gian và GeoJSON | Input sai: `400`; lỗi ORS: `422/502/503/504` theo tài liệu tuyến đi bộ |

Các endpoint `{id:int}` dùng ID số nguyên; ID không dương không tìm thấy POI. Body preview cần `startLatitude`, `startLongitude`, `poiIds` (mảng số nguyên dương, 1–49 ID; điểm xuất phát chiếm một waypoint trong giới hạn 50 của ORS). ID trùng bị từ chối `400`, không tự bỏ trùng. Khi có ID thiếu hoặc không công khai, toàn bộ preview bị từ chối, response Problem Details có `unavailablePoiIds`; không tiết lộ ID đó thiếu hay bị ẩn và không trả lộ trình một phần.

Response JSON dùng camelCase:

| Response | Trường |
| --- | --- |
| Một phần tử danh sách | `id`, `name`, `latitude`, `longitude`, `proximityRadius`, `thumbnailUrl`, `originalLanguage` |
| Chi tiết | Các trường danh sách và `originalDescription` |
| Một phần tử nearby | Các trường danh sách và `distanceMeters` |
| Nội dung | `poiId`, `requestedLanguage`, `actualLanguage`, `isFallback`, `text`, `audio` |
| Audio nếu có | `id`, `language`, `url`; nếu không có thì `null` |
| Preview | `start`, `stops`, `totalDistanceMeters`, `totalDurationSeconds`, `geometry`, `algorithm: "nearestNeighbor"`, `distanceType: "walking"`, `profile: "foot-walking"`, `provider: "openrouteservice"`, `attribution` |
| Mỗi stop | `order` bắt đầu từ 1, `poiId`, `name`, `latitude`, `longitude`, `distanceFromPreviousMeters`, `durationFromPreviousSeconds` (hai trường cuối lấy từ các segment ORS) |

Lỗi validation trả `application/problem+json` với `status: 400`, `errors` theo trường. Lỗi bất ngờ trả Problem Details `500` chung, không kèm exception/stack trace/cấu hình. Response không serialize entity hay navigation. Không dùng API trả lỗi để suy ra dữ liệu SQL đã được kiểm tra thành công.

### Tọa độ và khoảng cách

Latitude phải hữu hạn trong `[-90,90]`, longitude hữu hạn trong `[-180,180]`, `radiusMeters` hữu hạn và lớn hơn 0. Thiếu tham số, NaN, infinity hoặc chuỗi không phải số đều `400`. Không có vị trí hay bán kính mặc định. Khi nhập query dùng dấu chấm cho phần thập phân.

Haversine dùng bán kính Trái Đất `6.371.000` mét. Nearby giữ điểm có `distanceMeters <= radiusMeters`, sắp xếp theo khoảng cách rồi ID tăng dần khi bằng nhau. Khoảng cách không được làm tròn trước khi lọc. `ProximityRadius` (mét) là bán kính kích hoạt riêng của POI, không thay thế bán kính tìm kiếm từ tọa độ người dùng.

Backend chỉ tính từ tọa độ được gửi: có thể từ GPS hoặc điểm người dùng chọn trên bản đồ để mô phỏng. Backend không xác nhận GPS thật và không lưu vị trí mô phỏng.

Preview chọn thứ tự bằng Haversine nearest-neighbor từ `startLatitude`/`startLongitude`, sau đó tiếp tục từ POI vừa chọn; khi bằng khoảng cách chọn ID nhỏ hơn. Cùng tọa độ xuất phát và cùng tập ID cho cùng thứ tự ghé, kể cả khi đảo thứ tự gửi ID hoặc thứ tự hàng từ database khác nhau. Sau đó backend gửi `[điểm xuất phát, các POI đã sắp xếp]` tới ORS để lấy tuyến đi bộ. Không lặp POI, không thêm chặng quay về và không dùng ORS Optimization để đổi thứ tự. Khoảng cách/thời gian trả ra thuộc tuyến đi bộ ORS; không phải tổng Haversine. Đây vẫn là gợi ý thứ tự, không đảm bảo tối ưu toàn cục. Lỗi ORS không có fallback đường chim bay.

Preview chỉ đọc POI bằng một truy vấn theo danh sách ID, không tạo `SESSION`, `LO_TRINH`, sự kiện hay log database. Danh sách/lân cận đọc một truy vấn; nội dung tối đa ba truy vấn (POI, bản dịch nếu cần, audio), không truy vấn N+1.

## Gọi thử bằng PowerShell

Ví dụ không chứa ID/username/token thật. ID được lấy từ response đang chạy; không giả định ID là `1..8`. Chỉ thực hiện phần cần POI sau khi bạn đã nhập dữ liệu của mình.

```powershell
$demoBase = 'https://localhost:7239'
$demoPois = @(Invoke-RestMethod "$demoBase/api/pois" | ForEach-Object { $_ })
$demoPois | Select-Object id, name, latitude, longitude, proximityRadius
# Database trống: response JSON là [], không phải lỗi.
if ($demoPois.Count -eq 0) { throw 'Chưa có POI Published; hãy nhập dữ liệu đã xác nhận trong SSMS.' }
$demoPoiId = [int]$demoPois[0].id
Invoke-RestMethod "$demoBase/api/pois/$demoPoiId"
Invoke-RestMethod "$demoBase/api/pois/$demoPoiId/content?language=vi"
Invoke-RestMethod "$demoBase/api/pois/$demoPoiId/content?language=en"
```

Request nội dung trả `requestedLanguage`, `actualLanguage`, `isFallback`, `text`, `audio`. Ví dụ khi yêu cầu `en` mà chỉ có mô tả gốc `vi`: `requestedLanguage = en`, `actualLanguage = vi`, `isFallback = true`; audio chỉ có thể là audio tiếng Việt `Ready`, hoặc `null`.

Nhập điểm xuất phát/tìm kiếm do bạn chọn (dấu chấm thập phân). Cùng tọa độ có thể dùng để mô phỏng trên bản đồ:

```powershell
$demoLat = [double]::Parse((Read-Host 'Latitude'), [Globalization.CultureInfo]::InvariantCulture)
$demoLng = [double]::Parse((Read-Host 'Longitude'), [Globalization.CultureInfo]::InvariantCulture)
$demoRadius = [double]::Parse((Read-Host 'Bán kính tìm kiếm (mét)'), [Globalization.CultureInfo]::InvariantCulture)
$demoNearbyUrl = [string]::Format([Globalization.CultureInfo]::InvariantCulture,
    '{0}/api/pois/nearby?latitude={1:R}&longitude={2:R}&radiusMeters={3:R}',
    $demoBase, $demoLat, $demoLng, $demoRadius)
Invoke-RestMethod $demoNearbyUrl

# Chọn tối đa 3 ID thực từ danh sách để thử; thay bằng các ID bạn muốn ghé.
$demoIds = @($demoPois | Select-Object -First 3 | ForEach-Object { [int]$_.id })
$demoBody = @{
    startLatitude = $demoLat
    startLongitude = $demoLng
    poiIds = $demoIds
} | ConvertTo-Json
# Cần cấu hình OpenRouteService:ApiKey ở backend trước khi preview thành công.
$demoPreview = Invoke-RestMethod "$demoBase/api/routes/preview" -Method Post `
    -ContentType 'application/json' -Body $demoBody
$demoPreview | ConvertTo-Json -Depth 6
```

Các request lỗi dễ kiểm tra bằng curl (không cần token):

```powershell
curl.exe -i 'https://localhost:7239/api/pois/nearby' # 400: thiếu tham số
curl.exe -i 'https://localhost:7239/api/pois/nearby?latitude=NaN&longitude=0&radiusMeters=10' # 400
curl.exe -i 'https://localhost:7239/api/pois/0' # 404
curl.exe -i 'https://localhost:7239/api/pois/0/content?language=not_a_language' # 400
```

Không thêm `-k` để bỏ qua chứng chỉ. Các ví dụ du khách không gửi token; kiểm tra đăng nhập vẫn thực hiện theo tài liệu riêng.

## Nhập dữ liệu SQL thủ công

Chưa tìm thấy `insert_8_poi_saigon.sql` trong workspace ở lần bàn giao này. Cần bạn cung cấp script với tọa độ/nội dung thật để kiểm tra đầy đủ 8 POI; không tự điền tọa độ, mô tả, bản dịch hoặc URL audio. Ảnh chưa có có thể để `NULL`. Ngôn ngữ gốc dự kiến của script là `vi`.

Bạn tự mở script trong SSMS, kết nối `.`, chọn `AutoDubbingDb`, đối chiếu dữ liệu và chủ động thực thi. Giữ khóa ngoại `POI.nguoi_dung_id` bắt buộc; script phải tìm Admin theo **username bạn chỉ định**, không chọn Admin đầu tiên. Có thể kiểm tra ID bằng câu chỉ đọc sau, thay chỗ giữ chỗ bằng username chính xác, phân biệt hoa/thường:

```sql
USE AutoDubbingDb;
DECLARE @Username nvarchar(100) = N'<username Admin của bạn>';
SELECT id, ten_dang_nhap, trang_thai
FROM dbo.NGUOI_DUNG
WHERE ten_dang_nhap = @Username;

SELECT id, ten, trang_thai, ngon_ngu_goc, lat, lng, ban_kinh_kich_hoat
FROM dbo.POI
ORDER BY id;
```

Nếu không tìm thấy Admin hoặc dữ liệu script không phù hợp schema, dừng nhập và kiểm tra trước. API không có endpoint mới để tạo Admin/POI. Không tạo tài khoản mẫu để giải quyết khóa ngoại. Sau khi tự nhập dữ liệu, chạy lại các request ở trên với ID lấy từ truy vấn hoặc API.

## Kiểm tra và giới hạn bàn giao

Test tự động mới dùng HTTP TestServer và SQLite in-memory riêng; test toán khoảng cách chạy trực tiếp. Fixture bảo vệ bằng kiểm tra provider SQLite trước khi ghi. Không ghi fixture vào SQL Server dự án. Các test đăng nhập cũ vẫn nằm trong cùng suite.

Các ca mới gồm: dữ liệu trống; chỉ trả Published; thiếu/ẩn POI; tọa độ thiếu/sai/NaN/infinity; khoảng cách 0, ranh giới bán kính và thứ tự; ngôn ngữ gốc/bản dịch/fallback; chọn bản mới nhất; audio thiếu/sai ngôn ngữ/sai URL; payload preview thiếu/rỗng/trùng ID; ID ẩn/thiếu; nearest-neighbor từ POI vừa ghé; hòa khoảng cách; tổng chặng và không quay về; preview không ghi database; lỗi 500 không lộ exception.

Kết quả thực tế ngày 20/10/2026:

| Kiểm tra | Kết quả |
| --- | --- |
| Build solution `--no-restore` | Thành công, 0 lỗi; 4 cảnh báo NU1900 do nguồn audit không truy cập được. Không còn cảnh báo C# trong source mới. |
| Suite test đầy đủ | **106/106 đạt**, 0 lỗi, 0 bỏ qua; gồm 33 test đăng nhập cũ và 73 test mới. |
| EF `has-pending-model-changes --no-build` | Không có thay đổi model chưa đưa vào migration. |
| Git diff thư mục migration | Không thay đổi. |
| SQL Server thật | Cả `InitialCreate` và `ConfigureAdminAuthentication` đã áp dụng; POI, bản dịch và audio đều trống. |
| HTTP với SQL Server thật | **9/9 ca đạt**, chi tiết bên dưới. |
| Docker / CI từ xa | Chưa chạy; Docker CLI không có trên PATH. |

Report tự động ở `artifacts/test-results/backend-demo.trx`; report HTTP ở `artifacts/test-results/backend-demo-sqlserver.json`, đều được Git bỏ qua. Các ca HTTP SQL Server thật:

| Ca | HTTP thực tế |
| --- | --- |
| Danh sách khi trống | `200 []` |
| Nearby hợp lệ khi trống | `200 []` |
| Chi tiết ID không tồn tại | `404` |
| Nội dung ID không tồn tại, language hợp lệ | `404` |
| Language không hợp lệ | `400` |
| Nearby thiếu tham số | `400` |
| Nearby latitude NaN | `400` |
| Preview danh sách rỗng | `400` |
| Preview ID không tồn tại | `400` |

API được chạy ngoài sandbox để Windows Authentication hoạt động; request du khách dùng profile `http` tại `http://localhost:5133`, không gửi mật khẩu/JWT. Với profile chỉ HTTP, middleware báo không xác định được cổng HTTPS và request được phục vụ HTTP; đây chỉ là smoke test local cho endpoint ẩn danh. API đã được dừng sau kiểm tra. Lượt chạy trong sandbox trước đó gặp lỗi quyền DPAPI/Windows Event Log và timeout; chỉ ghi nhận bảng trên sau khi chạy lại thành công ngoài sandbox. Không sửa source xác thực hoặc bỏ kiểm tra chứng chỉ để xử lý giới hạn sandbox.

Ở lần bàn giao trước khi có dữ liệu, **chưa kiểm tra dữ liệu đầy đủ trên SQL Server:** lọc POI ẩn khi có bản ghi, chọn bản dịch/audio, preview thành công với nhiều POI. Các ca đó đã đạt bằng fixture SQLite. Kết quả sau khi người dùng nhập dữ liệu được cập nhật ở cuối tài liệu. Không ghi dữ liệu thử vào `AutoDubbingDb`. SQLite đạt không thay thế kiểm tra dữ liệu đầy đủ trên SQL Server. Login thành công bằng mật khẩu Admin thật và ca Locked trên SQL Server vẫn theo tiến độ trong `admin-auth.md`, không suy ra từ các ca du khách.

NuGet Audit vẫn bật, `System.Security.Cryptography.Pkcs` vẫn ghim `9.0.21`; không thêm package. Restore bằng cache chạy được nhưng nguồn NuGet không truy cập được trong môi trường hiện tại, phát sinh `NU1900`. Vì vậy chưa xác nhận được audit mới nhất. Khi mạng cho phép, chạy lại restore và kiểm tra:

```powershell
dotnet restore AutoDubbingSystem.slnx --force-evaluate
dotnet list AutoDubbingSystem.slnx package --vulnerable --include-transitive --no-restore
```

CI đã thêm bước chạy test sau build Release. Chưa chạy CI từ xa. Dockerfile hiện đã copy cả csproj test để restore solution; không cần sửa. Máy hiện không tìm thấy Docker CLI nên chưa build image hoặc kiểm tra runtime container. Khi có Docker:

```powershell
docker build -t autodubbing-api .
```

Build image không chứng minh kết nối SQL Server hoạt động. Windows Authentication với server `.` trên máy Windows không tự hoạt động trong Linux container. Chưa đổi cơ chế xác thực SQL hay cấu hình hạ tầng để chạy container.

## File trong đợt triển khai này

- `AutoDubbing.API/Controllers/PoisController.cs`, `RoutesController.cs`: HTTP du khách.
- `AutoDubbing.API/Program.cs`: đăng ký repository/service và lỗi 500 chung.
- `AutoDubbing.Service/Pois/`: DTO, query, quy tắc ngôn ngữ và đọc nội dung/lân cận.
- `AutoDubbing.Service/Routes/`: validation, DTO và thuật toán preview.
- `AutoDubbing.Service/Geography/GeoMath.cs`, `Validation/`: Haversine và kiểm tra số hữu hạn.
- `AutoDubbing.Data/Entities/PoiReadStatuses.cs`, `Repositories/IPoiReadRepository.cs`, `PoiReadRepository.cs`: hằng trạng thái và truy vấn đọc.
- `AutoDubbing.Tests/DemoApiFixture.cs`, `GeoMathTests.cs`, `PoiReadTests.cs`, `PoiContentTests.cs`, `RoutePreviewTests.cs`, `PublicErrorTests.cs`: kiểm thử mới.
- `.github/workflows/ci.yml`: chạy suite test trong CI.
- `docs/backend-demo.md`: hướng dẫn này.

Không sửa migration, entity/schema hiện có hay dependency. Giữ thay đổi xóa `AutoDubbing.API/AutoDubbing.API.http` và file prompt chưa commit đã có trước đợt này. Không commit/push/merge hoặc chuyển nhánh.

## Cập nhật sau khi người dùng nhập SQL — 20/10/2026

Đã đọc lại SQL Server thật sau khi người dùng báo bổ sung SQL: có đủ 8 POI, ID thực tế `1..8`, ngôn ngữ gốc `vi`, nhưng tất cả đang có `trang_thai = published` (chữ thường). Chưa có bản dịch hoặc audio. Chưa tìm thấy file SQL nhập POI trong workspace; kết quả này được xác nhận bằng SELECT trực tiếp, không phải đọc script nhập.

Do API so sánh trạng thái phân biệt hoa/thường và chỉ phục vụ `Published`, 8 bản ghi hiện chưa được công khai. Không tự sửa dữ liệu hoặc nới quy tắc API. Người dùng có thể tự chuẩn hóa đúng các bản ghi vừa kiểm tra trong SSMS:

```sql
USE AutoDubbingDb;
UPDATE dbo.POI
SET trang_thai = N'Published'
WHERE id IN (1, 2, 3, 4, 5, 6, 7, 8)
  AND trang_thai COLLATE Latin1_General_100_BIN2 = N'published';

SELECT id, ten, trang_thai FROM dbo.POI ORDER BY id;
```

Lệnh UPDATE trên là hướng dẫn để người dùng chủ động thực hiện; agent chưa chạy. Sau khi chuẩn hóa, cần kiểm tra lại HTTP danh sách/chi tiết/lân cận, nội dung gốc/fallback với `audio: null` và preview thành công. Không tạo bản dịch/audio chỉ để phục vụ kiểm thử. Các kết quả 9/9 HTTP trong bảng bàn giao trước thuộc thời điểm database còn trống, không đại diện cho lượt kiểm tra sau nhập dữ liệu này.

## Xác minh sau khi người dùng chuẩn hóa Published — 20/10/2026

Người dùng đã chủ động chạy UPDATE trong SSMS. SELECT trực tiếp xác nhận có 8 POI, ID thực tế `1..8`, tất cả `Published`, ngôn ngữ gốc `vi`; chưa có bản dịch hoặc audio. API chạy ngoài sandbox với Windows Authentication, profile `http` tại `http://localhost:5133`. Các request chỉ dùng endpoint du khách ẩn danh, không gửi mật khẩu hoặc JWT, không bỏ qua kiểm tra chứng chỉ HTTPS.

Kết quả cuối: **39/39 ca HTTP đạt trên SQL Server thật**:

| Nhóm | Số ca | Kết quả |
| --- | --- | --- |
| Danh sách | 1 | `200`, đủ 8 ID đúng thứ tự, không có thông tin Admin |
| Chi tiết | 8 | Tất cả `200`, đúng tên/tọa độ và có mô tả gốc |
| Nearby | 4 | Khoảng cách 0 tại POI; bán kính 1/300/5000 mét lọc và sắp xếp đúng; vị trí xa trả `[]` |
| Nội dung | 16 | Mỗi POI được kiểm tra `vi` và `en`: văn bản đúng mô tả gốc; `vi` không fallback, `en` fallback về `vi`; mọi audio là `null` |
| Preview thành công | 2 | Đủ 8 POI theo nearest-neighbor và tổng chặng đúng; một POI tại điểm xuất phát có tổng khoảng cách 0 |
| Preview lỗi | 3 | ID thiếu lẫn ID hợp lệ bị từ chối toàn bộ; trùng ID và mảng rỗng đều `400` |
| Lỗi đọc/query | 5 | POI thiếu `404`; nội dung POI thiếu `404`; language sai, nearby thiếu tham số và NaN đều `400` |

Điểm xuất phát kiểm tra là tọa độ thực đang lưu của POI 1: `(10.7798, 106.699)`, dùng làm điểm mô phỏng; không phải vị trí thiết bị. Đã gửi danh sách ID đảo thứ tự để kiểm tra backend tự tính lộ trình. Thứ tự kết quả:

`1 → 2 → 6 → 7 → 5 → 3 → 4 → 8`

Tương ứng Nhà thờ Đức Bà → Bưu điện Trung tâm → Nhà hát Thành phố → Phố đi bộ Nguyễn Huệ → Chợ Bến Thành → Dinh Độc Lập → Bảo tàng Chứng tích Chiến tranh → Thảo Cầm Viên. Tổng **4256.294682773578 mét đường chim bay**, không có chặng quay về. Khoảng cách được đối chiếu bằng công thức cosin cầu độc lập trong script kiểm tra, dung sai dưới 0,25 mét mỗi chặng; tổng response được đối chiếu với tổng các chặng.

Request có thể gọi lại sau khi khởi động profile `https`:

```json
{
  "startLatitude": 10.7798,
  "startLongitude": 106.699,
  "poiIds": [8, 7, 6, 5, 4, 3, 2, 1]
}
```

Gửi `POST https://localhost:7239/api/routes/preview` với `Content-Type: application/json`. ID trong ví dụ là các ID đã đọc thật trong lần kiểm tra này; nếu dữ liệu thay đổi, đọc lại ID trước khi gọi.

SELECT trước và sau kiểm tra xác nhận vẫn 8 POI `Published`; `BAN_DICH`, `AUDIO`, `SESSION`, `LO_TRINH`, `SU_KIEN_SU_DUNG`, `NHAT_KY_HE_THONG` đều 0 bản ghi. Không tự tạo dữ liệu, đổi trạng thái hoặc ghi lộ trình. API đã được dừng sau kiểm tra.

Report: `artifacts/test-results/backend-demo-populated-sqlserver.json`. Script chạy lại chỉ đọc: `artifacts/verify-populated-demo.ps1`, yêu cầu API đang chạy profile `http` tại `5133` và đúng bộ 8 POI hiện tại. Cả hai nằm trong thư mục artifacts được Git bỏ qua, có thể xóa sau khi không cần kiểm tra lại. Script ban đầu gặp lỗi chọn overload số nguyên của `Math.Min/Max` trong phép đối chiếu khoảng cách; đã sửa script và chạy lại, kết quả cuối 39/39, không thay đổi API.

Giới hạn còn lại: chưa có dữ liệu bản dịch/audio `Ready` để xác nhận việc chọn bản ghi và URL trên SQL Server thật; chưa có POI `Hidden` trong dữ liệu hiện tại để kiểm tra ca ẩn trên SQL Server. Các ca đó vẫn chỉ được xác nhận bằng fixture SQLite; không tự tạo hoặc sửa dữ liệu để kiểm tra. Không kiểm tra lại mật khẩu Admin/Locked, Docker hay audit trong đợt này. Không sửa source API, migration hoặc dependency; chỉ cập nhật tài liệu và tạo script/report tạm.
