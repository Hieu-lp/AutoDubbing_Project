import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

void i18n.use(initReactI18next).init({
  resources: {
    vi: { translation: { discover: 'Khám phá quanh bạn', subtitle: 'Những câu chuyện thú vị chỉ cách bạn vài bước chân.', near: 'Địa điểm gần bạn', nearby: 'địa điểm trong 500 m', locate: 'Vị trí của tôi', choose: 'Chọn vị trí', change: 'Đổi vị trí', loading: 'Đang tìm vị trí của bạn…', denied: 'Bạn chưa cấp quyền vị trí. Hãy chọn một điểm trên bản đồ để khám phá.', outside: 'Bạn đang ngoài khu vực demo Quận 1. Đang hiển thị các điểm quanh Nhà hát Thành phố.', gpsError: 'Chưa lấy được vị trí. Bạn có thể chọn vị trí trên bản đồ.', noPois: 'Chưa có địa điểm nào trong bán kính 500 m.', listen: 'Nghe thuyết minh', arrived: 'Bạn đã đến nơi', read: 'Đọc câu chuyện', back: 'Quay lại danh sách', accuracy: 'Độ chính xác GPS', demo: 'Vị trí demo · Nhà hát Thành phố', selected: 'Vị trí đã chọn trên bản đồ', gps: 'Vị trí hiện tại của bạn' } },
    en: { translation: { discover: 'Discover around you', subtitle: 'Fascinating stories just a few steps away.', near: 'Places near you', nearby: 'places within 500 m', locate: 'My location', choose: 'Choose location', change: 'Change location', loading: 'Finding your location…', denied: 'Location permission was not granted. Choose a point on the map to explore.', outside: 'You are outside the District 1 demo area. Showing places around the City Opera House.', gpsError: 'Could not get your location. You can choose a point on the map.', noPois: 'No places within 500 m.', listen: 'Listen to the story', arrived: 'You have arrived', read: 'Read the story', back: 'Back to places', accuracy: 'GPS accuracy', demo: 'Demo location · City Opera House', selected: 'Selected map location', gps: 'Your current location' } },
  },
  lng: 'vi',
  fallbackLng: 'vi',
  interpolation: { escapeValue: false },
});

export default i18n;
