// Run with the Playwright browser_run_code tool's filename input; local app listens on https://localhost:5079.
async (page) => {
  const context = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  try {
    const tab = await context.newPage(), base = 'https://localhost:5079';
    const check = (value, message) => { if (!value) throw new Error(message); };
    const token = 'A'.repeat(43), newToken = 'B'.repeat(42) + 'A', first = 'a'.repeat(64), second = 'b'.repeat(64);
    let signedIn = false, unavailable = false, records = [], revision = 0;
    const photo = (session, id = 'final') => ({ id, bytes: 1000, contentType: 'image/jpeg', fileName: id === 'final' ? 'picme-final.jpg' : 'picme-original-' + id,
      capturedUtc: '2026-11-15T12:00:00Z', previewUrl: '/brand/background2.webp', downloadUrl: `/api/event/${token}/media/${session}/${id}?download=true`, fileUrl: '/brand/background2.webp' });
    const session = hash => ({ sessionId: hash, final: photo(hash), sessionPath: `/e/${token}/s/${hash}` });
    await tab.route('**/api/admin/**', async route => {
      const url = new URL(route.request().url()), path = url.pathname, body = route.request().method() === 'POST' ? route.request().postDataJSON() : null;
      if (path.endsWith('/session')) return route.fulfill({ json: { authenticated: signedIn, username: signedIn ? 'admin' : null, csrfToken: 'browser-csrf' } });
      if (path.endsWith('/login')) {
        if (body.password !== 'browser-test-password') return route.fulfill({ status: 401, json: { message: 'Incorrect username or password.' } });
        signedIn = true; return route.fulfill({ json: { success: true } });
      }
      if (!signedIn) return route.fulfill({ status: 401, json: {} });
      if (path.endsWith('/logout')) { signedIn = false; return route.fulfill({ json: { success: true } }); }
      if (path === '/api/admin/events' && !body) return route.fulfill({ json: { events: records, nextCursor: null } });
      if (path === '/api/admin/events') {
        records = [{ eventId: body.eventId, name: body.name, active: true, ownerPath: '/e/' + token, version: String(++revision) }];
        return route.fulfill({ json: { ownerPath: records[0].ownerPath } });
      }
      const revoke = path.endsWith('/revoke');
      records[0] = { ...records[0], active: !revoke, ownerPath: revoke ? null : '/e/' + newToken, version: String(++revision) };
      return route.fulfill({ json: { ownerPath: records[0].ownerPath } });
    });
    await tab.route('**/api/event/**', route => {
      if (unavailable) return route.fulfill({ status: 404, json: { message: 'Unavailable' } });
      const url = new URL(route.request().url());
      if (url.pathname.includes('/sessions/')) return route.fulfill({ json: { state: 'ready', expectedOriginals: 2, uploadedOriginals: 2,
        final: photo(first), originals: [photo(first, '001.jpg'), photo(first, '002.jpg')] } });
      return route.fulfill({ json: { name: 'Anna & David’s celebration', sessions: [session(url.searchParams.has('cursor') ? second : first)], nextCursor: url.searchParams.has('cursor') ? null : 'next-page', downloadAllUrl: `/api/event/${token}/download` } });
    });
    const errors = [];
    tab.on('pageerror', error => errors.push(error.message));
    await tab.goto(base + '/admin/login');
    await tab.locator('#username').fill('admin'); await tab.locator('#password').fill('wrong'); await tab.locator('#sign-in').click();
    await tab.waitForFunction(() => document.getElementById('admin-status').textContent.includes('Incorrect'));
    await tab.locator('#password').fill('browser-test-password'); await tab.locator('#sign-in').click();
    await tab.locator('#events-panel').waitFor({ state: 'visible' });
    await tab.locator('#event-name').fill('Anna & David’s celebration'); await tab.locator('#event-id').fill('anna-david'); await tab.locator('#create-button').click();
    await tab.locator('.event-row').waitFor();
    check(await tab.locator('#new-owner-url').inputValue() === base + '/e/' + token, 'Owner link uses public browser origin');
    await tab.getByRole('button', { name: 'Replace link', exact: true }).click();
    await tab.locator('#cancel-action').click();
    check(records[0].ownerPath === '/e/' + token, 'Cancelling replacement leaves link unchanged');
    await tab.getByRole('button', { name: 'Replace link', exact: true }).click(); await tab.locator('#accept-action').click();
    await tab.waitForFunction(expected => document.getElementById('new-owner-url').value.endsWith(expected), newToken);
    await tab.getByRole('button', { name: 'Revoke link', exact: true }).click(); await tab.locator('#accept-action').click();
    await tab.waitForFunction(() => document.querySelector('.event-row .subtext')?.textContent.includes('revoked'));
    for (const width of [1440, 390, 320]) {
      await tab.setViewportSize({ width, height: 900 });
      check(await tab.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Admin responsive layout: ' + width);
      await tab.screenshot({ path: '/tmp/picme-admin-' + width + '.png', fullPage: true });
    }
    await tab.locator('#logout').click(); await tab.locator('#login-panel').waitFor({ state: 'visible' });
    await tab.screenshot({ path: '/tmp/picme-admin-login.png', fullPage: true });
    await tab.goto(base + '/e/' + token);
    await tab.locator('#event-photos .photo-card').waitFor();
    check(await tab.locator('#event-photos img:not(.download-icon)').count() === 1, 'Main gallery shows only finals');
    await tab.locator('#more-photos').click(); await tab.locator('#event-photos .photo-card').nth(1).waitFor();
    check(await tab.locator('#more-photos').isHidden(), 'Last page hides pagination control');
    check(await tab.locator('#download-all').getAttribute('href') === `/api/event/${token}/download`, 'Download all is independent of visible page');
    await tab.getByRole('button', { name: 'Preview moment 1', exact: true }).click();
    await tab.locator('#event-preview[open]').waitFor();
    await tab.keyboard.press('Escape');
    check(await tab.evaluate(() => document.activeElement.getAttribute('aria-label') === 'Preview moment 1'), 'Owner preview restores focus');
    for (const width of [1440, 390, 320]) {
      await tab.setViewportSize({ width, height: 900 });
      check(await tab.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Owner responsive layout: ' + width);
      await tab.screenshot({ path: '/tmp/picme-owner-' + width + '.png', fullPage: true });
    }
    await tab.getByRole('link', { name: 'All photos for moment 1', exact: true }).click();
    await tab.locator('#originals .photo-card').nth(1).waitFor();
    check(await tab.locator('#event-back').isVisible(), 'Owner session has event navigation');
    check(await tab.locator('#refresh').isHidden(), 'Completed owner session hides Check for photos');
    await tab.locator('#preview-final').click(); await tab.keyboard.press('ArrowRight');
    check(await tab.locator('#preview-title').textContent() === 'Original photo 1', 'Owner can preview session originals');
    await tab.keyboard.press('Escape'); await tab.locator('#event-back').click();
    await tab.locator('#event-photos .photo-card').waitFor(); unavailable = true; await tab.locator('#refresh-event').click();
    await tab.waitForFunction(() => document.getElementById('event-title').textContent === 'Link unavailable');
    check(await tab.locator('#event-photos .photo-card').count() === 0 && await tab.locator('#download-all').isHidden(), 'Revoked link clears gallery and download');
    check(errors.length === 0, 'No browser script errors: ' + errors.join(', '));
    return { passed: ['login error/success/logout', 'create/copy-path/replace/cancel/revoke', 'admin and owner responsive layouts', 'final-only owner grid and pagination', 'final preview and focus', 'originals in session viewer', 'revoked link recovery'], errors };
  } finally { await context.close(); }
}
