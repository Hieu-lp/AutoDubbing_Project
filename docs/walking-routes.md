# Tuyến đi bộ OpenRouteService cho backend

## Lộ trình gợi ý độc lập (27/10/2026)

`LO_TRINH` không còn Session cha hay `session_id`. Admin quản lý bằng
`GET/POST /api/admin/routes`, `PUT/DELETE /api/admin/routes/{id}` (JWT Admin
Active). Tên, mảng ID POI theo thứ tự, trạng thái Published/Hidden được lưu
SQL. Xóa mềm giữ lịch sử sử dụng, không xóa POI. Không xuất bản được lộ trình
chứa ID trùng, không tồn tại hoặc POI ẩn.

Khách đọc `GET /api/routes`: chỉ route Published có đủ POI công khai/hợp lệ.
Chọn **Lộ trình gợi ý** trên web rồi dùng vị trí GPS hoặc điểm xuất phát đã
chọn. `POST /api/routes/{id}/preview` nhận startLatitude/startLongitude; server
tự đọc danh sách POI đã lưu, gọi cùng ORS provider và **giữ thứ tự admin**
(`algorithm=adminOrder`). Nếu route thay đổi trong lúc tính đường, trả 409;
nếu đã ẩn/xóa hoặc POI không còn công khai, không trả preview hợp lệ.

Khách tự chọn POI vẫn dùng `POST /api/routes/preview`: sắp xếp nearest-neighbor
theo khoảng cách Haversine rồi lấy đường phố từ ORS. Đây là heuristic, không
cam kết tìm tối ưu toàn cục. Không vẽ đường giả khi provider lỗi.

Danh sách gợi ý đọc lại mỗi lần mở, khi focus và mỗi 10 giây khi đang mở/đã
chọn. Đổi tên/thứ tự được phản ánh; đường cũ bị gỡ và có hướng dẫn tính lại.
Ẩn/xóa hoặc POI mất công khai xóa lựa chọn/đường cũ và thông báo rõ. Không
cần WebSocket hoặc xóa cache trình duyệt. Sự kiện route_selection chỉ gửi
sau khi tạo đường thành công; lỗi thống kê không cản hiển thị đường.

`POST /api/routes/preview` được cập nhật để trả tuyến đi bộ `foot-walking` từ OpenRouteService (ORS), sử dụng dữ liệu OpenStreetMap. Đây là hợp đồng hiện tại; response `straightLine` và kết quả 39/39 HTTP ở giai đoạn trước không chứng minh tuyến ORS đã được kiểm tra. Không triển khai frontend, không đổi schema, không ghi dữ liệu dự án và không thêm package.

## Điểm xuất phát và thứ tự ghé

Client bắt buộc gửi `startLatitude`, `startLongitude` ở hệ tọa độ địa lý WGS84, cùng `poiIds`. Backend không tự lấy vị trí máy chủ, không xác nhận GPS và không chọn POI đầu tiên làm điểm xuất phát. Tọa độ có thể đến từ GPS thiết bị hoặc một điểm người dùng chủ động chọn để mô phỏng; frontend sau này phải gửi rõ điểm đã chọn. Không có vị trí mặc định. Các ví dụ tọa độ dưới đây là điểm mô phỏng, không phải vị trí thật của thiết bị.

Latitude hữu hạn trong `[-90,90]`, longitude hữu hạn trong `[-180,180]`; thiếu, NaN, infinity và input sai trả `400`. `poiIds` chứa 1–49 số nguyên dương duy nhất; ID trùng bị từ chối. ORS công khai hiện giới hạn [50 waypoint cho Directions](https://openrouteservice.org/restrictions/), bao gồm điểm xuất phát. ID thiếu hoặc POI không `Published` bị từ chối toàn bộ bằng `400` cùng `unavailablePoiIds`; không trả lộ trình một phần.

Backend đọc POI từ SQL Server bằng một truy vấn và sắp xếp nearest-neighbor bằng Haversine. Mỗi bước chọn điểm chưa ghé gần nhất từ vị trí hiện tại; hòa khoảng cách chọn ID nhỏ nhất. Kết quả không phụ thuộc thứ tự ID trong request hoặc thứ tự hàng từ database. Chỉ **thứ tự ghé** được đảm bảo ổn định; ORS có thể thay đổi geometry/thời gian khi dữ liệu đường được cập nhật. Không đảm bảo thứ tự tối ưu toàn cục theo khoảng cách đi bộ.

Sau khi sắp xếp, gửi toàn bộ `[điểm xuất phát, POI thứ nhất, ... POI cuối]` trong một request ORS, không quay về và không yêu cầu ORS tối ưu lại waypoint. Không gộp bỏ POI có cùng tọa độ: mọi POI được giữ; nếu ORS từ chối các điểm này thì trả lỗi, không tạo geometry thay thế. Nearby tiếp tục dùng Haversine và `ProximityRadius` vẫn là bán kính kích hoạt riêng của POI.

## Request ORS và dữ liệu trả về

Endpoint cố định của provider:

```text
POST https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson
Authorization: <API key chỉ ở backend>
Accept: application/geo+json
Content-Type: application/json
```

Địa chỉ này theo [thông báo chuyển API chính thức của ORS](https://ask.openrouteservice.org/t/deprecating-api-openrouteservice-org-in-favour-of-api-heigit-org/7912), thay cho domain `api.openrouteservice.org` đã deprecated. Body gửi `coordinates` theo **`[longitude, latitude]`**, `units: "m"`, `instructions: true`, `elevation: false`. Cần bật instructions vì [ORS loại bỏ segments khi tắt instructions](https://github.com/GIScience/openrouteservice/blob/main/ors-engine/src/main/java/org/heigit/ors/routing/RouteResultBuilder.java); backend chỉ giữ metric của segment, không trả chỉ dẫn từng bước. Không gửi key trong URL hoặc JSON; không gửi tên POI/thông tin Admin tới ORS. Không tự retry để tránh tiêu quota lặp; không tự theo redirect mang theo credentials.

Theo [tài liệu response của ORS](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/requests-and-return-types), GeoJSON chứa `FeatureCollection`, geometry `LineString`, `summary` và các `segments` giữa waypoint. Backend chỉ sao chép geometry 2D và các số khoảng cách/thời gian hợp lệ. Không chuyển tiếp response thô, `metadata.query`, error message hay header upstream. Tổng và các chặng được kiểm tra hợp lệ, hữu hạn, không âm; số segment phải khớp số POI.

Response API:

| Trường | Ý nghĩa |
| --- | --- |
| `start.latitude`, `start.longitude` | Điểm client gửi, không thay bằng tọa độ POI |
| `stops[]` | POI theo thứ tự ghé, mỗi ID xuất hiện một lần |
| `stops[].order` | Thứ tự bắt đầu từ 1 |
| `stops[].poiId`, `name`, `latitude`, `longitude` | Dữ liệu POI đọc từ SQL Server |
| `stops[].distanceFromPreviousMeters` | Khoảng cách segment đi bộ ORS từ điểm trước, đơn vị mét |
| `stops[].durationFromPreviousSeconds` | Thời gian segment ORS, đơn vị giây |
| `totalDistanceMeters` | Tổng khoảng cách đi bộ ORS, mét |
| `totalDurationSeconds` | Tổng thời gian ước tính ORS, giây; không thêm thời gian dừng tham quan |
| `geometry` | GeoJSON `{ "type": "LineString", "coordinates": [[longitude, latitude], ...] }` |
| `algorithm` | `nearestNeighbor` (thứ tự ghé dùng Haversine) |
| `distanceType`, `profile`, `provider` | `walking`, `foot-walking`, `openrouteservice` |
| `attribution` | Chuỗi ghi công ORS/OSM để frontend hiển thị |

Geometry là tuyến do ORS trả, không nối thẳng các POI trong backend. ORS có thể snap điểm vào mạng đường, nên đỉnh geometry không nhất thiết trùng hoàn toàn marker POI. Giữ nguyên `[longitude, latitude]` khi đưa geometry vào bộ đọc GeoJSON của Leaflet; không đảo mảng thành `[latitude, longitude]`. Tọa độ object `start`/`stops` dùng tên trường rõ ràng. Thời gian là ước tính của provider, không phải cam kết thời gian thực.

## Cấu hình key trên máy của bạn

Lấy key trong [HeiGIT Account](https://account.heigit.org/). Không gửi key vào chat, không dùng key frontend và không ghi key vào `appsettings.json`, source, Dockerfile hoặc request mẫu. Project đã có UserSecretsId; không cần tạo lại.

Tại workspace trong PowerShell, nhập key ẩn rồi lưu qua stdin để key không xuất hiện trong câu lệnh/lịch sử terminal:

```powershell
$orsSecret = Read-Host 'ORS API key (nhập cục bộ)' -AsSecureString
$orsCredential = New-Object System.Management.Automation.PSCredential('ORS', $orsSecret)
@{ 'OpenRouteService:ApiKey' = $orsCredential.GetNetworkCredential().Password } |
    ConvertTo-Json | dotnet user-secrets set --project AutoDubbing.API
Remove-Variable orsCredential, orsSecret

dotnet run --project AutoDubbing.API --launch-profile https
```

Không chạy `user-secrets list` để gửi kết quả vì nó in key. User Secrets được nạp trong Development. Khi dùng biến môi trường, tên tương ứng là `OpenRouteService__ApiKey`. Ví dụ cấp key cho tiến trình backend trong terminal hiện tại:

```powershell
$orsSecret = Read-Host 'ORS API key (nhập cục bộ)' -AsSecureString
$orsCredential = New-Object System.Management.Automation.PSCredential('ORS', $orsSecret)
$env:OpenRouteService__ApiKey = $orsCredential.GetNetworkCredential().Password
Remove-Variable orsCredential, orsSecret
try {
    dotnet run --project AutoDubbing.API --launch-profile https
}
finally {
    Remove-Item Env:\OpenRouteService__ApiKey -ErrorAction SilentlyContinue
}
```

Backend vẫn cần JWT signing key local như [admin-auth.md](admin-auth.md). Các API POI/đăng nhập vẫn khởi động bình thường nếu thiếu ORS key; chỉ preview hợp lệ trả `503 routing_not_configured`. Timeout mặc định `OpenRouteService:TimeoutSeconds = 20`, có thể đặt từ 1 đến 120 giây; giá trị ngoài khoảng trả lỗi cấu hình khi tạo tuyến. Khởi động lại API sau khi đổi cấu hình. Không có key mặc định hoặc key mẫu có thể gọi ORS thật.

## Gọi thử API của backend

Sau khi cấu hình key và HTTPS local đáng tin cậy, lấy ID thật từ API. Không giả định ID liên tiếp:

```powershell
$walkingBase = 'https://localhost:7239'
$walkingPois = @(Invoke-RestMethod "$walkingBase/api/pois" | ForEach-Object { $_ })
if ($walkingPois.Count -eq 0) { throw 'Chưa có POI Published.' }
$walkingPois | Select-Object id, name
$walkingLat = [double]::Parse((Read-Host 'Latitude xuất phát'), [Globalization.CultureInfo]::InvariantCulture)
$walkingLng = [double]::Parse((Read-Host 'Longitude xuất phát'), [Globalization.CultureInfo]::InvariantCulture)
$walkingIds = @($walkingPois | Select-Object -First 3 | ForEach-Object { [int]$_.id })
$walkingBody = @{
    startLatitude = $walkingLat
    startLongitude = $walkingLng
    poiIds = $walkingIds
} | ConvertTo-Json
$walkingResult = Invoke-RestMethod "$walkingBase/api/routes/preview" -Method Post `
    -ContentType 'application/json' -Body $walkingBody
$walkingResult | Select-Object totalDistanceMeters, totalDurationSeconds, provider, profile, attribution
$walkingResult.stops | Format-Table order, poiId, name, distanceFromPreviousMeters, durationFromPreviousSeconds
$walkingResult.geometry | ConvertTo-Json -Depth 5
```

Mã phản hồi thành công là `200`. Giá trị geometry/thời gian chỉ có sau khi ORS trả thành công; không có ví dụ đường đi hoặc số thời gian giả làm kết quả dịch vụ thật trong tài liệu. Đảo mảng `$walkingIds` và gọi lại với cùng điểm xuất phát để kiểm tra thứ tự ghé giữ nguyên.

## Lỗi và hành vi fallback

Lỗi định dạng/input/POI trả `400` trước khi gọi ORS. Các lỗi provider trả Problem Details có `code` ổn định:

| HTTP | `code` | Tình huống |
| --- | --- | --- |
| `503` | `routing_not_configured` | Thiếu key, key có khoảng trắng/ký tự điều khiển không hợp lệ, hoặc timeout cấu hình sai |
| `503` | `routing_quota_exceeded` | ORS `429`, hoặc `403` có thông báo quota |
| `503` | `routing_provider_rejected` | ORS `401`/`403` khác; kiểm tra key/quyền truy cập trong dashboard |
| `504` | `routing_timeout` | Quá thời hạn khi gửi request hoặc đọc response body |
| `422` | `walking_route_not_found` | ORS code `2009`/`2010`, hoặc không có feature tuyến |
| `422` | `routing_request_rejected` | ORS từ chối request (`400`/`413` khác, ví dụ giới hạn độ dài tuyến) |
| `502` | `routing_invalid_response` | JSON/GeoJSON, metric hoặc số segment không hợp lệ |
| `502` | `routing_unavailable` | Lỗi kết nối, upstream lỗi/redirect hoặc response vượt giới hạn bộ đệm |

Một số thông báo `403` của ORS có thể gộp quota và key không hợp lệ; cần đối chiếu dashboard để xác định nguyên nhân. Backend không đưa thông báo thô vào response/log. Không tự retry hoặc ghép một phần đường đi khi một waypoint không tìm được tuyến. Request bị client hủy được truyền cancellation tới HTTP provider.

**Không fallback đường chim bay.** Khi lỗi không trả `geometry`, `stops` hoặc các số khoảng cách/thời gian giả. Response thành công chỉ dùng metric/geometry của ORS, kể cả khi điểm đầu và POI trùng tọa độ. POI không bị đổi tọa độ hoặc trạng thái để làm tuyến chạy được.

## Attribution cho frontend sau này

API trả chuỗi:

`© openrouteservice.org by HeiGIT | Map data © OpenStreetMap contributors`

Frontend phải hiển thị attribution dễ thấy cùng bản đồ/kết quả tuyến, có liên kết ORS và OSM copyright; không chỉ giữ trong response hoặc console. Tham khảo [điều khoản ORS hiện hành](https://account.heigit.org/info/tos), [mục attribution ORS](https://staging.openrouteservice.org/terms-of-service/) và [OSM copyright/licensing](https://www.openstreetmap.org/copyright). Với OSM, liên kết trang copyright giúp người xem truy cập thông tin ODbL. Giữ cả attribution của nhà cung cấp tile được chọn sau này; attribution của tuyến không thay thế attribution của tile.

Nội dung liên kết có thể dùng cho attribution control của Leaflet sau này:

```html
© <a href="https://openrouteservice.org/">openrouteservice.org</a> by HeiGIT |
Map data © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap contributors</a>
```

Đợt này chỉ tài liệu hóa hợp đồng và attribution; không tạo frontend hoặc chọn dịch vụ tile.

## Kiểm thử và giới hạn

Provider được kiểm tra bằng `HttpMessageHandler` giả, không gọi ORS thật. Test host luôn ghi đè ORS key bằng giá trị test hoặc `null`, thay handler HTTP bằng stub ngay cả khi môi trường CI có biến key. SQLite in-memory tách biệt dùng cho test API; không ghi SQL Server dự án. Geometry và metric trong stub chỉ là fixture, không phải kết quả đường đi thật hoặc fallback sản phẩm.

Các ca gồm tọa độ ORS `[longitude, latitude]`, header key không có Bearer, GeoJSON/thời gian/segment, metadata không được chuyển tiếp, key không có trong response/log; missing key, quota, 401/403, timeout cả body, client cancellation, lỗi mạng, không tìm được tuyến, JSON/geometry/metric sai; hoán vị toàn bộ sáu thứ tự của ba POI có hòa khoảng cách; đảo thứ tự repository; đổi điểm xuất phát; Published/Hidden và preview không ghi database.

Chạy build và suite trong workspace:

```powershell
dotnet build AutoDubbingSystem.slnx --no-restore
dotnet test AutoDubbing.Tests/AutoDubbing.Tests.csproj --no-build --no-restore `
    --logger 'trx;LogFileName=ors-backend.trx' --results-directory artifacts/test-results
```

CI dùng cùng suite với HTTP stub, không cần ORS key. Ở lần bàn giao code chưa gọi ORS thật; kết quả thiết lập và kiểm tra local sau đó được cập nhật ở cuối tài liệu. Không in key local hoặc yêu cầu gửi key vào chat. Kết quả mock không xác nhận độ phủ mạng đường, khả năng snap từng POI hoặc thời gian đi bộ thật. Script cũ `artifacts/verify-populated-demo.ps1` đối chiếu khoảng cách đường chim bay là bằng chứng lịch sử trước ORS, không dùng làm kiểm tra tuyến đi bộ hiện tại.

File thay đổi trong đợt ORS: `AutoDubbing.Service/Routes/` (provider, options, interface/model, service và DTO), `AutoDubbing.API/Controllers/RoutesController.cs`, `Program.cs`, `appsettings.json`; test factory/fixture, các test route/provider; tài liệu này và `backend-demo.md`. Không sửa Data, migrations, dependency, Docker hoặc pipeline CI đã có bước test.

## Kết quả bàn giao — 20/10/2026

| Kiểm tra thực tế | Kết quả |
| --- | --- |
| `dotnet build AutoDubbingSystem.slnx --no-restore` | Thành công; 0 lỗi, 4 cảnh báo NU1900 do không truy cập được nguồn dữ liệu audit NuGet; không có cảnh báo C# |
| Suite đầy đủ sau thay đổi cuối | **162/162 đạt**, 0 lỗi, 0 bỏ qua; gồm 33 test đăng nhập và các test POI/route/provider |
| Report cuối | `artifacts/test-results/ors-backend-final.trx` |
| Script PowerShell trong tài liệu | 4 khối trong tài liệu này và 6 khối trong `backend-demo.md` đều parse được, 0 lỗi cú pháp; không thực thi các khối chứa thao tác cấu hình key |
| Diff Data/model/migrations/csproj | Không thay đổi trong đợt ORS; không thêm package hoặc migration |
| ORS thật / SQL Server với tuyến đi bộ thật | Chưa kiểm tra; không đọc key hoặc gọi Directions thật trong đợt này |
| CI từ xa / Docker | Không chạy trong đợt này |

NuGet Audit vẫn bật và bản vá Pkcs hiện có được giữ nguyên. Chưa xác minh được dữ liệu audit mới nhất; không suppress NU1900 hay tắt audit. Không đổi dữ liệu SQL Server, không chạy create-admin, commit/push/merge hoặc chuyển nhánh.

Danh sách file của đợt ORS:

- Sửa `AutoDubbing.API/Program.cs`, `appsettings.json`, `Controllers/RoutesController.cs`.
- Sửa `AutoDubbing.Service/Routes/RoutePreviewService.cs`, `RoutePreviewDtos.cs`.
- Thêm `AutoDubbing.Service/Routes/IWalkingRouteProvider.cs`, `OrsDirectionsOptions.cs`, `OrsDirectionsProvider.cs`.
- Sửa `AutoDubbing.Tests/AdminApiFactory.cs`, `DemoApiFixture.cs`, `RoutePreviewTests.cs`.
- Thêm `AutoDubbing.Tests/OrsHttpStub.cs`, `OrsDirectionsProviderTests.cs`, `WalkingRouteApiTests.cs`, `RouteOrderingTests.cs`.
- Sửa `docs/backend-demo.md`, thêm `docs/walking-routes.md`.

Các thay đổi chưa commit có sẵn từ đợt backend trước vẫn được giữ, bao gồm file prompt ở gốc và việc xóa `AutoDubbing.API/AutoDubbing.API.http`.

## Thiết lập và chạy local với key thật — 20/10/2026

Theo yêu cầu cấu hình/chạy của người dùng, đã mở hộp nhập ẩn trên máy và lưu key ORS qua stdin vào User Secrets; JWT signing key hiện có được giữ nguyên. Không đặt key trong command line, source hoặc file workspace. Key nhập lần đầu bị ORS từ chối `403`; sau khi người dùng tự lấy và nhập lại key từ HeiGIT, đã khởi động lại backend và gọi Directions thành công. Không đổi endpoint hoặc thêm fallback để vượt lỗi quyền truy cập.

Backend được khởi động nền, profile `https`, tại `https://localhost:7239`; `http://localhost:5133` chuyển hướng HTTPS. GET POI bằng trust mặc định của Windows trả `200`, không bỏ kiểm tra TLS. Các URL dùng trực tiếp:

- Danh sách POI: `https://localhost:7239/api/pois`.
- OpenAPI JSON: `https://localhost:7239/openapi/v1.json`.
- Preview: `POST https://localhost:7239/api/routes/preview`, `Content-Type: application/json`.

Kết quả tuyến **ORS thật**, đọc 8 POI hiện có trong SQL Server:

| Thuộc tính | Kết quả |
| --- | --- |
| HTTP backend | `200` |
| Provider / profile | `openrouteservice` / `foot-walking` |
| Điểm xuất phát mô phỏng | Tọa độ POI 1: latitude `10.7798`, longitude `106.699`; không phải GPS thiết bị |
| Thứ tự ghé | `1 → 2 → 6 → 7 → 5 → 3 → 4 → 8`, đủ 8 POI, không lặp, không quay về |
| Tổng khoảng cách đi bộ | **6416.7 mét** |
| Thời gian ước tính | **4620 giây = 77 phút**, chưa gồm thời gian tham quan |
| Geometry | GeoJSON `LineString`, **249 điểm**, tọa độ `[longitude, latitude]` |
| Key trong response/log backend | Không phát hiện |

Đã kiểm tra số điểm dừng, ID không lặp, geometry và tổng các segment nhất quán với summary. Không dùng khoảng cách đường chim bay ở giai đoạn trước làm khoảng cách đi bộ. Các con số trên là kết quả một lần gọi thực tế; ORS có thể trả kết quả khác khi dữ liệu đường thay đổi.

Request/response để xem lại nằm trong `artifacts/ors-route-local.request.json`, `artifacts/ors-route-local.response.json`; báo cáo không chứa key ở `artifacts/test-results/ors-local-runtime.json`. Thông tin tiến trình và URL nằm trong `artifacts/backend-local-process.json`, log ở `artifacts/logs/backend-local.stdout.log` và `.stderr.log`. Các file này được Git bỏ qua. Backend được giữ chạy sau bàn giao; để dừng đúng tiến trình đang ghi nhận, đọc `ApiProcessIds` trong file thông tin tiến trình và kiểm tra process trước khi dừng. PID có thể thay đổi sau khi khởi động lại, không dùng PID cũ tùy ý.

Không thực thi INSERT/UPDATE hoặc migration khi cấu hình/chạy. Không thêm account, bản dịch, audio hoặc dữ liệu mẫu. Không commit/push/merge. Các ví dụ đọc mảng POI bằng PowerShell đã được sửa để hoạt động trên Windows PowerShell 5.1, gồm trường hợp 0/1/nhiều phần tử; source backend không thay đổi trong bước vận hành này.
