import { defineConfig, devices } from '@playwright/test';

const port = process.env.E2E_PORT ?? '5181';
const baseURL = process.env.E2E_BASE_URL ?? `http://127.0.0.1:${port}`;

const webServerCommand = [
  'ASPNETCORE_ENVIRONMENT=Development',
  `ASPNETCORE_URLS=${baseURL}`,
  'dotnet run --project ../../src/AzureAIProxy.Admin/AzureAIProxy.Admin.csproj --no-launch-profile'
].join(' ');

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  timeout: 60_000,
  expect: {
    timeout: 10_000
  },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure'
  },
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: webServerCommand,
        url: `${baseURL}/account/login`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
        stdout: 'pipe',
        stderr: 'pipe'
      },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
});
