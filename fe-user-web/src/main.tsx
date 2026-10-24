import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import App from './App';
import 'leaflet/dist/leaflet.css';
import './i18n';
import './styles.css';
import './integration.css';

const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 60_000, retry: false, refetchOnWindowFocus: false } } });

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}><App /></QueryClientProvider>
  </React.StrictMode>,
);
