import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import App from './App';
import 'leaflet/dist/leaflet.css';
import './i18n';
import './styles.css';
import './admin.css';

const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 60_000, retry: 1 } } });
const AdminApp = React.lazy(() => import('./AdminApp'));
const isAdminPage = window.location.pathname.replace(/\/+$/, '') === '/admin';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      {isAdminPage ? <React.Suspense fallback={<main className="admin-login-page">Đang tải…</main>}><AdminApp /></React.Suspense> : <App />}
    </QueryClientProvider>
  </React.StrictMode>,
);
