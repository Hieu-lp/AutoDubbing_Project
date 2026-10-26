import { defineConfig, devices } from '@playwright/test';

if (process.env.CI && process.env.LIVE_API === '1') {
  throw new Error('Live API checks are manual only; CI must use mocked providers.');
}

export default defineConfig({
  testDir: './e2e',
  testMatch: process.env.LIVE_API === '1' ? '**/live*.spec.mjs' : '**/visitor.spec.mjs',
  timeout: 90_000,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { ...devices['Desktop Chrome'], baseURL: 'http://127.0.0.1:5174',
    channel: process.env.PLAYWRIGHT_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined),
    reducedMotion: 'reduce', launchOptions: { args: ['--no-proxy-server', '--mute-audio'] }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: { command: 'npm run dev -- --port 5174', url: 'http://127.0.0.1:5174', reuseExistingServer: !process.env.CI, timeout: 90_000 },
});
