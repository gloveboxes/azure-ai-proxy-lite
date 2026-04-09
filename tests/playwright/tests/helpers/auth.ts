import { expect, type Page } from '@playwright/test';

const defaultUsername = process.env.E2E_ADMIN_USERNAME ?? 'admin';
const defaultPassword = process.env.E2E_ADMIN_PASSWORD ?? 'admin';

export async function login(page: Page, username = defaultUsername, password = defaultPassword): Promise<void> {
  await page.goto('/account/login');
  await expect(page.locator('form[name="loginForm"]')).toBeVisible();

  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('input[type="submit"][value="Sign In"]').click();

  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByText('AI Proxy Admin').first()).toBeVisible();
}
