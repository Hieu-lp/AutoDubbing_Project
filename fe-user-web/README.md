# VoiceMap — web du khách

React 19, Vite 7, React Query và Leaflet. Giữ bố cục giao diện hiện có; POI, nội dung, ngôn ngữ và lộ trình đều lấy từ AutoDubbing API. Không có Admin dashboard hoặc key provider trong frontend.

## Chạy local

Cần .NET 10, SQL Server/backend đã cấu hình, Node **22.20 trở lên có `--use-system-ca`**, npm và chứng chỉ HTTPS development đã được tin cậy. Nếu các server đang chạy thì dùng trực tiếp, không chạy thêm instance trên cùng cổng.

Terminal backend, từ thư mục repository:

```powershell
dotnet run --project AutoDubbing.API --launch-profile https -- --ContentPreparation:AcceptNewGeneration=false
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

Ngưỡng **500 m / 50 m / 3 giây / polling 3 giây** đọc từ GET settings, không lấy `ProximityRadius` SQL để thay ngưỡng này. Không sửa bán kính trong database. `ContentPreparation:AcceptNewGeneration=false` là cấu hình backend; frontend không có công tắc bật Gemini.

## Luồng đã nối

- GET POI và marker thật; chạm danh sách/marker để đọc chi tiết. GET `/api/languages` cung cấp 15 lựa chọn. GET nội dung trả `actualLanguage/isFallback` được hiển thị rõ. Không giả rằng bản gốc là bản dịch của ngôn ngữ đã chọn.
- Nội dung đúng ngôn ngữ và đã có audio được dùng ngay. Thiếu mới POST prepare-content; manager dùng chung chống bấm lặp, giữ phần đọc được, chặn gửi thêm khi nhận GenerationPaused. Nút kiểm tra lại trước hết đọc GET.
- Job 202 được polling theo Retry-After/settings, tối đa 10 phút, hủy theo POI/ngôn ngữ/vùng quan tâm và khi rời trang/dừng thuyết minh. Job 404 đọc lại nội dung một lần, không tự re-enqueue. Lỗi quota/429 không có retry POST liên tục; request sau phải chờ cooldown và có hành động mới.
- Chọn nhiều POI và một vị trí xuất phát GPS hoặc trên map để preview. Backend quyết định thứ tự; đường vẽ, khoảng cách, thời gian và attribution dùng response GeoJSON/ORS. Không gọi Valhalla/ORS trực tiếp, không dựng đường chim bay khi ORS lỗi. Khoảng cách nhỏ trong danh sách chỉ là khoảng cách vị trí gần đúng và không thay metric tuyến đi bộ.
- “Vị trí của tôi” lấy một fix để khám phá/lập tuyến. **“Bắt đầu thuyết minh”** mới mở watcher và phiên tự phát. Vị trí chọn trên map luôn là điểm khám phá thủ công, không phải bằng chứng khách đứng ở đó và không kích hoạt tự phát.
- Khi GPS tin cậy đưa POI vào 500 m, gộp POST nearby, mỗi POI một lần/phiên, cách các lượt mới ít nhất 10 giây; chỉ chuẩn bị ngôn ngữ nguồn. Không POST từng GPS tick hoặc tự tạo 14 ngoại ngữ.
- Trong 50 m liên tục đủ dwell mới xét phát nguồn; mất vị trí/ra vùng trước dwell hủy đếm. Khi audio về muộn, kiểm tra lại fix mới, ứng viên gần nhất, phiên còn hoạt động, lựa chọn hiện tại và trạng thái player. Một audio và tối đa một lần tự phát/POI/phiên; phát lại thủ công được phép. Dừng rồi bắt đầu lại là một phiên tham quan mới.
- Chọn ngoại ngữ là lựa chọn nghe chủ động: tải nội dung tương ứng, yêu cầu phần thiếu và thử phát khi Ready. Response cũ không ghi đè lựa chọn mới. Nếu browser chặn play, hiện hướng dẫn chạm Phát. Fallback có audio gốc cần chọn rõ “Nghe bản gốc”.
- Dừng thuyết minh/rời màn hình dọn GPS, timer, polling và player. Khi ẩn tab, hủy dwell và polling không còn được quan tâm. Audio đã phát có thể tiếp tục khi đi ra vùng; mọi lượt tự phát chưa bắt đầu luôn phải thỏa vị trí mới. Thay POI/ngôn ngữ hoặc dừng phiên sẽ dừng audio hiện tại.

Sau GenerationPaused, frontend giữ trạng thái tạm dừng trong vòng đời trang để tránh tạo request lặp. Khi người vận hành sau này đổi trạng thái backend, tải lại trang. Đợt tích hợp này luôn giữ backend tạm dừng, không xác nhận luồng sinh thật 202 → Ready.

## Tự demo

1. Mở trang, chọn VI/EN cho giao diện. Kiểm tra 8 POI và marker; mở Nhà thờ Đức Bà, đổi lần lượt 15 ngôn ngữ, phát/dừng/seek. Nút dropdown chọn ngôn ngữ có thể thử phát; chỉ một player hoạt động.
2. Mở Bưu điện, chọn en: vẫn đọc bản gốc vi với nhãn fallback, audio thiếu hiện “Nội dung hiện chưa sẵn sàng”. Không bấm gửi lặp để dò quota.
3. Chọn vị trí trên map hoặc dùng GPS, mở Tạo lộ trình, chọn ít nhất hai điểm rồi tạo. Thứ tự sau preview là thứ tự backend trả. Nếu ORS lỗi, hiện thông báo và giữ lựa chọn, không dựng tuyến giả.
4. Desktop DevTools → Sensors: mô phỏng `10.7798, 106.699`, sai số khoảng 5 m. Trở về danh sách và bấm Bắt đầu thuyết minh. Chờ fix tin cậy và đủ 3 giây; nếu bị chặn, chạm Phát. Đây là mô phỏng, không phải kiểm chứng GPS ngoài thực địa.
5. Thử đi ra ngoài 50 m trước 3 giây, đặt vị trí cũ/sai số cao hoặc từ chối quyền; không được tự phát. Thử đổi ngôn ngữ/POI khi đang tải, quay lại và nghe thủ công. Bấm Dừng thuyết minh trước khi kết thúc.

## Kiểm tra

```powershell
npm run lint
npm test
npm run build
npm run test:e2e
```

Unit test dùng Node test runner. Suite Edge mặc định mock toàn bộ API/audio/GPS; có case 202 nhưng đó **không phải** pipeline Gemini thật. Windows dùng Edge cài sẵn; môi trường khác dùng browser Playwright tương ứng (`PLAYWRIGHT_CHANNEL` nếu cần).

Suite tích hợp thật chỉ chạy chủ động sau khi xác minh backend tạm dừng; có GET nội dung/audio, POST prepare đang paused và một request preview ORS thật:

```powershell
$env:LIVE_API='1'
npm run test:e2e
Remove-Item Env:LIVE_API
```

Không chạy suite live trong CI. Test live vẫn mô phỏng geolocation bằng công cụ trình duyệt, không chứng nhận GPS thực tế hoặc chất lượng bản dịch/giọng đọc. Edge headless được tắt tiếng đầu ra trong test; việc có metadata/play không phải nghe thẩm định.

Giới hạn backend giữ nguyên: một instance; queue/jobId/cooldown mất khi restart; bản dịch lịch sử chưa có revision nguồn trong schema. Hợp đồng chi tiết: [nội dung theo yêu cầu](../docs/content-on-demand.md), [tuyến ORS](../docs/walking-routes.md).
