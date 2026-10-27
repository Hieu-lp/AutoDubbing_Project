import React, { useEffect } from 'react';
import App from './App';
import './admin.css';

const AdminApp = React.lazy(() => import('./AdminApp'));

export default function RootApp() {
  const isAdminPage = window.location.pathname.replace(/\/+$/, '') === '/admin';

  useEffect(() => {
    document.title = isAdminPage ? 'VoiceMap — Quản trị địa điểm' : 'VoiceMap — Khám phá quanh bạn';
  }, [isAdminPage]);

  if (!isAdminPage) return <App />;

  return (
    <React.Suspense fallback={<main className="admin-login-page">Đang tải…</main>}>
      <AdminApp />
    </React.Suspense>
  );
}
