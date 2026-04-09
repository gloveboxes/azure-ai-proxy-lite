import { expect, test } from '@playwright/test';

test.describe('Admin anonymous access', () => {
  test('redirects protected pages to login with return URL', async ({ page }) => {
    await page.goto('/events');

    await expect(page).toHaveURL(/\/account\/login\?ReturnUrl=%2Fevents/);
    await expect(page.locator('h2')).toContainText('AI Proxy Admin');
  });

  test('shows local login form controls', async ({ page }) => {
    await page.goto('/account/login');

    await expect(page.locator('h2')).toContainText('AI Proxy Admin');
    await expect(page.locator('input#username')).toBeVisible();
    await expect(page.locator('input#password')).toBeVisible();
    await expect(page.locator('input[type="submit"][value="Sign In"]')).toBeVisible();
  });

  test('rejects invalid credentials', async ({ page }) => {
    await page.goto('/account/login');

    await page.locator('#username').fill('wrong-user');
    await page.locator('#password').fill('wrong-password');
    await page.locator('input[type="submit"][value="Sign In"]').click();

    await expect(page).toHaveURL(/\/account\/login/);
    await expect(page.locator('.error-message')).toContainText('Invalid username or password.');
  });
});
