# VoiceMap — web du khách

React 19, Vite 7, React Query và Leaflet. Giữ bố cục giao diện hiện có; POI, nội dung, ngôn ngữ và lộ trình đều lấy từ AutoDubbing API. Không có Admin dashboard hoặc key provider trong frontend.

## Chạy local

Cần .NET 10, SQL Server/backend đã cấu hình, Node **22.20 trở lên có `--use-system-ca`**, npm và chứng chỉ HTTPS development đã được tin cậy. Nếu các server đang chạy thì dùng trực tiếp, không chạy thêm instance trên cùng cổng.

Terminal backend, từ thư mục repository:

```powershell
dotnet run --project AutoDubbing.API --launch-profile https
```

Terminal frontend:

```powershell
cd fe-user-web
npm ci
if (!(Test-Path .env.local)) { Copy-Item .env.example .env.local }
npm run dev
```

- Mở **http://localhost:5173**. API HTTPS: **https://localhost:7239**; OpenAPI: **https://localhost:7239/openapi/v1.json**.
- Vite proxy `/api` qua HTTPS tới backend, ép kết nối IPv4, kiểm tra TLS và dùng CA hệ thống. Không cần sửa CORS backend. Nếu chứng chỉ chưa tin cậy, chạy `dotnet dev-certs https --trust` trên đúng tài khoản rồi khởi động lại server; không tắt xác minh chứng chỉ.
- Vite preview: `npm run build` rồi `npm run preview`, URL **http://localhost:4173**. Production cần reverse proxy cùng origin cho `/api`; Vite dev/preview không phải server production.
- Trên điện thoại, giao diện responsive dùng được qua địa chỉ LAN do Vite in ra. GPS trình duyệt cần **HTTPS hoặc localhost**; HTTP qua IP LAN không đủ. Dùng HTTPS phù hợp môi trường khi demo GPS trên thiết bị thật.

## Cấu hình

Xem [.env.example](.env.example). Chỉ có cấu hình công khai, không đưa bất kỳ key Gemini/ORS/Supabase UploadKey/JWT signing key vào biến `VITE_*`.

| Biến | Mặc định | Vai trò |
| --- | --- | --- |
| `VITE_API_BASE_URL` | `/api` | Prefix HTTP frontend, ưu tiên cùng origin |
| `API_PROXY_TARGET` | `https://localhost:7239` | Đích proxy trong Node/Vite; không bundle vào browser |
| `VITE_GPS_MAX_AGE_MS` | `10000` | Từ chối vị trí cũ khi tự phát |
| `VITE_GPS_MAX_ACCURACY_METERS` | `50` | Sai số GPS tối đa cho chuẩn bị/tự phát |

Ngưỡng **500 m / 50 m / 3 giây / polling 3 giây** đọc từ GET settings, không lấy `ProximityRadius` SQL để thay ngưỡng này. Không sửa bán kính trong database. Frontend không có công tắc bật Gemini.

### Bật/tắt generation local

Chế độ demo bình thường hiện giữ **`ContentPreparation:AcceptNewGeneration=true`** trong user-secrets của dự án. Giá trị mặc định trong [appsettings.json](../AutoDubbing.API/appsettings.json) vẫn false; cấu hình local không đưa lên Git. API phải chạy môi trường Development như launch profile `https` ở trên để nạp user-secrets.

Từ repository root:

```powershell
# Bật và giữ qua các lần chạy local
dotnet user-secrets set "ContentPreparation:AcceptNewGeneration" "true" --project AutoDubbing.API

# Tạm dừng khi người vận hành cần; không xóa nội dung đã có
dotnet user-secrets set "ContentPreparation:AcceptNewGeneration" "false" --project AutoDubbing.API
```

Sau khi đổi cờ, khởi động lại đúng API khi queue đã hết công việc. Thứ tự ghi đè thông thường: tham số dòng lệnh > biến môi trường > user-secrets Development > cấu hình Development > cấu hình mặc định. Bỏ tham số `--ContentPreparation:AcceptNewGeneration=false` của các lệnh kiểm chứng cũ nếu muốn dùng chế độ demo. Có thể ép một lượt bằng `dotnet run --project AutoDubbing.API --launch-profile https -- --ContentPreparation:AcceptNewGeneration=true`; tham số đó cũng ghi đè lệnh tắt trong user-secrets cho đến lần restart sau.

Không dùng `dotnet user-secrets list` để chia sẻ cấu hình; chỉ đọc/ghi đúng cờ cần kiểm tra. Giữ nguyên model/key đã cấu hình. Các script trong artifacts của các lượt kiểm chứng trước có thể khôi phục false hoặc giới hạn request; không dùng chúng làm launcher demo bình thường.

## Luồng đã nối

- GET POI và marker thật; chạm danh sách/marker để đọc chi tiết. GET `/api/languages` cung cấp 15 lựa chọn. GET nội dung trả `actualLanguage/isFallback` được hiển thị rõ. Không giả rằng bản gốc là bản dịch của ngôn ngữ đã chọn.
- Nội dung đúng ngôn ngữ và đã có audio được dùng ngay. Thiếu mới POST prepare-content; manager dùng chung chống bấm lặp, giữ phần đọc được, chặn gửi thêm khi nhận GenerationPaused. Nút kiểm tra lại trước hết đọc GET.
- Job 202 được polling theo Retry-After/settings, tối đa 10 phút, hủy theo POI/ngôn ngữ/vùng quan tâm và khi rời trang/dừng thuyết minh. Job 404 đọc lại nội dung một lần, không tự re-enqueue. Lỗi quota/429 không có retry POST liên tục; request sau phải chờ cooldown và có hành động mới.
- Chọn nhiều POI và một vị trí xuất phát GPS hoặc trên map để preview. Backend quyết định thứ tự; đường vẽ, khoảng cách, thời gian và attribution dùng response GeoJSON/ORS. Không gọi Valhalla/ORS trực tiếp, không dựng đường chim bay khi ORS lỗi. Khoảng cách nhỏ trong danh sách chỉ là khoảng cách vị trí gần đúng và không thay metric tuyến đi bộ.
- “Vị trí của tôi” chuyển sang GPS và chờ cập nhật mới; khi đang thuyết minh sẽ theo dõi GPS liên tục. **“Chọn vị trí”** đặt điểm trên map làm vị trí du khách cho khoảng cách, nearby, tự phát và điểm xuất phát lộ trình. Callback GPS cũ không ghi đè điểm map. Điểm map đứng yên có hiệu lực đến khi đổi/chuyển về GPS, không áp dụng tuổi fix hoặc sai số GPS.
- **Xác nhận điểm map tự bắt đầu phiên thuyết minh**, không cần bấm thêm “Bắt đầu thuyết minh” hoặc mở chi tiết. Chọn thẳng điểm trong 50 m cũng được chuẩn bị nguồn và bắt đầu đếm 3 giây. Đổi điểm giữ phiên đang bật, hủy dwell/request/audio đang chờ và xét vị trí mới; giữ chống phát lặp trong phiên. Chạm marker chỉ xem POI, không đặt vị trí du khách. Nút Bắt đầu/Dừng vẫn dùng cho GPS hoặc chủ động dừng/bắt đầu lại.
- Khi vị trí du khách hợp lệ đưa POI vào 500 m (kể cả trong 50 m), gộp POST nearby, mỗi POI một lần/phiên; chỉ chuẩn bị ngôn ngữ nguồn. GPS gộp các lượt mới ít nhất 10 giây; xác nhận điểm map mới cho phép kiểm tra ngay các POI chưa xử lý, vẫn giữ dedup POI đã xử lý và cooldown 429. Request bị hủy khi đổi điểm không đánh dấu POI là đã chuẩn bị. Không POST từng tick đếm hoặc tự tạo 14 ngoại ngữ.
- Trong 50 m liên tục đủ dwell mới xét phát nguồn; mất vị trí/ra vùng trước dwell hủy đếm. Khi audio về muộn, kiểm tra lại vị trí hiện hành, ứng viên gần nhất, phiên còn hoạt động, lựa chọn hiện tại và trạng thái player. Một audio và tối đa một lần tự phát/POI/phiên; phát lại thủ công được phép. Dừng rồi bắt đầu lại là một phiên tham quan mới, giữ điểm map đã chọn.
- Chọn ngoại ngữ là lựa chọn nghe chủ động: tải nội dung tương ứng, yêu cầu phần thiếu và thử phát khi Ready. Response cũ không ghi đè lựa chọn mới. Nếu browser chặn play, hiện hướng dẫn chạm Phát. Fallback có audio gốc cần chọn rõ “Nghe bản gốc”.
- Dừng thuyết minh/rời màn hình dọn GPS, timer, polling và player. Khi ẩn tab, hủy dwell và polling không còn được quan tâm. Audio đã phát có thể tiếp tục khi đi ra vùng; mọi lượt tự phát chưa bắt đầu luôn phải thỏa vị trí mới. Thay POI/ngôn ngữ hoặc dừng phiên sẽ dừng audio hiện tại.

Sau GenerationPaused, frontend chặn yêu cầu sinh riêng lặp lại, nhưng vẫn kiểm tra nearby một lần cho POI mới và dùng nội dung Ready. Danh sách hiện rõ POI chưa có audio/tạo nội dung đang tạm dừng; mô tả vẫn đọc được. Chỉ hiện “Đang chuẩn bị” khi có job Queued/Processing thật. Sau khi bật lại generation hoặc restart API, **tải lại trang (F5)** để bỏ trạng thái paused/job cũ trong bộ nhớ frontend. TTS nguồn vi và luồng frontend dịch/TTS en của Thảo Cầm Viên đã được kiểm chứng thật đến Ready, upload/SQL và phát, tái sử dụng thành công.

### Quota và lỗi provider

Bật generation cho phép phát sinh lời gọi Gemini, upload Supabase và ghi metadata theo thao tác của người dùng; không tự sinh sẵn toàn bộ POI/ngôn ngữ. Ready được dùng lại ngay. Hệ thống giữ giới hạn 8 POST/client/phút, 30 POST/instance/phút, queue tối đa 32 job, một pipeline chạy tại một thời điểm; job timeout mặc định 10 phút. Provider retry hữu hạn theo cấu hình hiện có, không đổi model/key hoặc bật billing.

Nếu quota/provider/storage lỗi, giữ phần văn bản/audio/cache đã thành công và đọc thông báo; không bấm thử lại liên tục. `RateLimit` là giới hạn ngắn hạn, `DailyQuotaExceeded` là hạn mức ngày, `QuotaUnavailable` là allowance bằng 0 và cần người vận hành kiểm tra quyền/model/project. Không dùng restart để né cooldown/quota. Số lượt từng chạy thành công không chứng minh quota còn lại hoặc đảm bảo mọi ngôn ngữ sẽ sinh được.

Development console có các dòng `[tour]` cho nguồn/tọa độ, dwell bắt đầu/hủy, lý do bỏ qua request, trạng thái chuẩn bị và kết quả `play()`. Không log nội dung, secret hoặc URL query; production không phát các log này.

## Tự demo

1. Mở trang, F5 nếu đã dùng khi generation còn tắt. VI/EN ở đầu trang chỉ đổi giao diện; dropdown trong chi tiết chọn ngôn ngữ thuyết minh. Mở Nhà thờ Đức Bà hoặc Thảo Cầm Viên vi/en để nghe các bản Ready đã lưu, phát/dừng/seek; chỉ một player hoạt động.
2. Khi bạn muốn thử tạo phần còn thiếu, tự chọn một POI/ngôn ngữ mong muốn trên web. Frontend chỉ yêu cầu đúng ngôn ngữ đó; có thể đọc nguồn với nhãn fallback trong lúc chờ. Job thật hiện “Đang chuẩn bị”, được polling và tải nội dung/audio đúng ngôn ngữ khi Ready. Đổi lựa chọn trong lúc chờ không được phát response cũ. Lỗi quota/provider phải dừng chờ theo thông báo, không bấm gửi lặp.
3. Chọn vị trí trên map hoặc dùng GPS, mở Tạo lộ trình, chọn ít nhất hai điểm rồi tạo. Thứ tự sau preview là thứ tự backend trả. Nếu ORS lỗi, hiện thông báo và giữ lựa chọn, không dựng tuyến giả.
4. Xác nhận điểm map tự bắt đầu phiên: Published còn thiếu trong 500 m được chuẩn bị audio nguồn, gần trước theo queue hiện có. Chọn thẳng dưới 50 m vẫn bắt đầu đếm; ở liên tục đủ 3 giây thì thử phát khi Ready. Nếu còn xử lý, chờ kết quả và kiểm tra lại vị trí/điều kiện trước phát. Không cần mở chi tiết, bấm Bắt đầu hoặc đi qua vùng 50–500 m. Nếu browser chặn, lý do hiện cạnh nút Phát. Vị trí map là đầu vào do bạn chọn, không xác nhận hiện diện thực địa.
5. Trước khi đủ 3 giây, đổi sang điểm ngoài 50 m: hủy đếm; quay lại phải chờ đủ 3 giây mới. Thử đổi điểm nhanh, đứng yên trên 10 giây, dừng/bắt đầu lại và nội dung thiếu ở Bưu điện. Khi chuyển “Vị trí của tôi”, chờ GPS mới; callback GPS đến muộn không được đổi điểm map.
6. Desktop DevTools → Sensors có thể mô phỏng GPS `10.7798, 106.699`, sai số 5 m; thử fix cũ/sai số cao/từ chối quyền. Thử đổi ngôn ngữ/POI khi đang tải và nghe thủ công. Bấm Dừng thuyết minh khi xong. Các kiểm tra mô phỏng không thay kiểm chứng GPS ngoài thực địa.

## Kiểm tra

```powershell
npm run lint
npm test
npm run build
npm run test:e2e
```

Unit test dùng Node test runner. Suite Edge mặc định mock toàn bộ API/audio/GPS; có case 202 nhưng đó **không phải** pipeline Gemini thật. Windows dùng Edge cài sẵn; môi trường khác dùng browser Playwright tương ứng (`PLAYWRIGHT_CHANNEL` nếu cần).

Suite tích hợp thật cũ có ca giả định dữ liệu còn thiếu và generation đang tạm dừng, đồng thời có request ORS thật. **Không chạy toàn bộ suite live trên chế độ demo true để dò thử**, vì có thể tạo thêm nội dung hoặc không còn đúng giả định dữ liệu sau các lượt đã tạo thành công. Chỉ chạy từng ca phù hợp, kiểm tra phạm vi/dữ liệu trước và không để launcher kiểm chứng cũ tự đổi cờ demo. Ví dụ bộ chọn ca thủ công:

```powershell
$env:LIVE_API='1'
npm run test:e2e -- --grep "tên ca đã kiểm tra phù hợp"
Remove-Item Env:LIVE_API
```

Không chạy suite live trong CI. Test live vẫn mô phỏng geolocation bằng công cụ trình duyệt, không chứng nhận GPS thực tế hoặc chất lượng bản dịch/giọng đọc. Edge headless được tắt tiếng đầu ra trong test; việc có metadata/play không phải nghe thẩm định.

Các ca `LIVE map:` dùng đồng hồ và API/audio thật nhưng tự chọn điểm, có POST nearby và có giả định dữ liệu của lượt kiểm chứng cũ; chỉ chạy sau khi chuẩn bị phạm vi/cấu hình riêng phù hợp. Không chạy chúng để dò thêm dữ liệu khi demo true. Ca ép `NotAllowedError` và GPS mô phỏng được ghi nhãn; `position-evidence.json` ghi tọa độ, dwell, request và kết quả phát. Nếu sandbox chặn mạng tới kho audio, chạy từ PowerShell thông thường; Node cần `--use-system-ca` như launcher frontend. Không bỏ kiểm tra TLS hoặc thay audio thật bằng fixture để báo đạt.

Giới hạn backend giữ nguyên: một instance; queue/jobId/cooldown mất khi restart; bản dịch lịch sử chưa có revision nguồn trong schema. Hợp đồng chi tiết: [nội dung theo yêu cầu](../docs/content-on-demand.md), [tuyến ORS](../docs/walking-routes.md).
# Tích hợp quản trị và dữ liệu hiện hành (27/10/2026)

`/admin` có quản lý tài khoản, POI, bản dịch/audio, lộ trình gợi ý, sự kiện và
nhật ký SQL; không còn nghiệp vụ Session tham quan. Xem hướng dẫn hiện hành
ở [admin-dashboard.md](../docs/admin-dashboard.md). User mở **Lộ trình gợi ý**
để đọc lộ trình admin xuất bản; chọn điểm xuất phát trước rồi chọn gợi ý để
tính đường theo thứ tự đã lưu. Khách tự chọn POI vẫn dùng heuristic gần nhất.

Danh sách POI và dữ liệu POI đang mở được làm mới 10 giây; gợi ý làm mới khi
mở/focus và mỗi 10 giây khi đang xem/đã chọn. Route bị ẩn/xóa hoặc POI mất
công khai sẽ gỡ lựa chọn/đường cũ, không cần xóa browser cache. Không tự vẽ
đường mẫu nếu API/ORS lỗi. Sự kiện nghe chỉ gửi khi media `playing`, có mã
chống trùng; lỗi thống kê không cản audio. Admin nghe thử không tính lượt khách.

Để demo thật giữ `VITE_USE_MOCK_DATA=false`, API/DB cần migration mới. Container
18081 phục vụ build tĩnh và không tự cập nhật theo source. [Hướng dẫn Docker
và LAN/HTTPS bốn thiết bị](../docs/team-user-web-docker.md) phân biệt các bước
triển khai với kết quả kiểm thử. Playwright dùng cổng riêng 5174 và provider
fixture; không coi kết quả mock là chứng nhận dịch/TTS/GPS/thiết bị thật.
