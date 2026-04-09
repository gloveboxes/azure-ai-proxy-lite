# Playwright E2E Tests (Admin App)

This folder contains end-to-end tests for the Admin Blazor app.

## Dev Container Workflow

These tests are intended to run from this repository's dev container.

- Playwright dependencies and browser runtime libraries are installed on demand.
- Azurite is available via the dev container compose setup.

Before your first E2E run (or whenever you need to refresh), run from repo root:

```bash
npm run e2e:install
```

## Architecture notes

- Supported: Linux arm64 and Linux amd64.
- First-time setup is heavier on arm64 because additional compatibility components may be installed.
- First-time setup on amd64 is typically faster because fewer compatibility packages are needed.
- Normal test runs do not reinstall dependencies unless you run `npm run e2e:install` again.

## What is covered

- Anonymous flow:
  - Protected route redirects to login.
  - Login form renders.
  - Invalid credentials are rejected.
- Authenticated flow (opt-in):
  - Local login success.
  - Main nav links are visible.
  - Navigation to Events, Resources, and Backup pages.
  - Deep-link return URL behavior after login.
  - Logout behavior.

## Run tests

From the repository root (recommended):

```bash
npm run e2e:test
```

Run authenticated suite too:

```bash
E2E_RUN_AUTH_TESTS=true npm run e2e:test
```

Run in single worker mode (recommended for container stability):

```bash
E2E_RUN_AUTH_TESTS=true npm run e2e:test -- --workers=1
```

You can also run directly from tests/playwright:

```bash
cd tests/playwright
npm test
```

Run authenticated suite too:

```bash
E2E_RUN_AUTH_TESTS=true npm test
```

Use a different target app instance:

```bash
E2E_BASE_URL=http://127.0.0.1:8901 npm test
```

Override login credentials used by authenticated tests:

```bash
E2E_ADMIN_USERNAME=admin E2E_ADMIN_PASSWORD=admin E2E_RUN_AUTH_TESTS=true npm test
```

## Notes

- If `E2E_BASE_URL` is not set, Playwright starts the Admin app automatically with:
  - `ASPNETCORE_ENVIRONMENT=Development`
  - `ASPNETCORE_URLS=http://127.0.0.1:5181`
- Authenticated tests are skipped unless `E2E_RUN_AUTH_TESTS=true`.
