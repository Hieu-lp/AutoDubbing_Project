import { defineConfig, loadEnv } from 'vite';
import { Agent } from 'node:https';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'API_');
  const target = process.env.API_PROXY_TARGET ?? env.API_PROXY_TARGET ?? 'https://localhost:7239';
  const proxy = { '/api': { target, changeOrigin: true, secure: true, agent: target.startsWith('https:') ? new Agent({ family: 4 }) : undefined } };
  return {
    plugins: [react(), tailwindcss()],
    build: { rollupOptions: { output: { manualChunks(id) {
      const name = id.replaceAll('\\', '/');
      if (name.includes('node_modules/leaflet/') || name.includes('node_modules/react-leaflet/') || name.includes('node_modules/@react-leaflet/')) return 'map';
      if (name.includes('node_modules/')) return 'vendor';
    } } } },
    server: { host: '0.0.0.0', port: 5173, strictPort: true, proxy,
      watch: { ignored: ['**/playwright-report/**', '**/test-results/**', '**/e2e/**', '**/tests/**'] } },
    preview: { host: '0.0.0.0', port: 4173, strictPort: true, proxy },
  };
});
