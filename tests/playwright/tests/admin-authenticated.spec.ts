import { expect, test } from '@playwright/test';
import { login } from './helpers/auth';

const runAuthenticatedSuite = process.env.E2E_RUN_AUTH_TESTS === 'true';

test.describe('Admin authenticated flows', () => {
  test.skip(!runAuthenticatedSuite, 'Set E2E_RUN_AUTH_TESTS=true to run authenticated E2E tests.');

  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('shows main navigation after login', async ({ page }) => {
    await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Events' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Resources' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Reports' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Backup' })).toBeVisible();
  });

  test('navigates to events, resources and backup pages', async ({ page }) => {
    await page.getByRole('link', { name: 'Events' }).click();
    await expect(page).toHaveURL(/\/events/);
    await expect(page.getByRole('link', { name: 'New Event' })).toBeVisible();

    await page.getByRole('link', { name: 'Resources' }).click();
    await expect(page).toHaveURL(/\/models/);
    await expect(page.getByRole('link', { name: 'New Resource' })).toBeVisible();

    await page.getByRole('link', { name: 'Backup' }).click();
    await expect(page).toHaveURL(/\/backup/);
    await expect(page.getByRole('button', { name: 'Backup Data' })).toBeVisible();
    await expect(page.getByText('Restore Data', { exact: true })).toBeVisible();
  });

  test('returns to original deep link after login', async ({ page }) => {
    await page.goto('/account/logout');
    await expect(page).toHaveURL(/\/account\/login/);

    await page.goto('/models');
    await expect(page).toHaveURL(/\/account\/login\?ReturnUrl=%2Fmodels/);

    await page.locator('#username').fill(process.env.E2E_ADMIN_USERNAME ?? 'admin');
    await page.locator('#password').fill(process.env.E2E_ADMIN_PASSWORD ?? 'admin');
    await page.locator('input[type="submit"][value="Sign In"]').click();

    await expect(page).toHaveURL(/\/models/);
    await expect(page.getByRole('link', { name: 'New Resource' })).toBeVisible();
  });

  test('logs out and requires login again', async ({ page }) => {
    await page.getByRole('link', { name: 'Log out' }).click();
    await expect(page).toHaveURL(/\/account\/login/);

    await page.goto('/backup');
    await expect(page).toHaveURL(/\/account\/login\?ReturnUrl=%2Fbackup/);
  });
});
