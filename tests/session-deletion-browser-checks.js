// Run through Playwright browser_run_code against the local app on port 5079.
async (page) => {
  const context = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  try {
    const admin = await context.newPage(), base = 'https://localhost:5079', hash = 'a'.repeat(64), eventId = 'deletion-test';
    const html = await (await admin.request.get(base + '/admin-sessions.html')).body();
    const check = (value, label) => { if (!value) throw new Error(label); };
    const errors = [];
    admin.on('pageerror', error => errors.push(error.message));
    let version = 1, deleted = false, listRequests = 0, imageRequests = 0, mutations = 0, available = 2, failOriginals = false;
    await context.route('**/admin/events/*/sessions', route => route.fulfill({ body: html, contentType: 'text/html' }));
    await context.route('**/api/admin/**', async route => {
      const url = new URL(route.request().url()), path = url.pathname;
      if (path === '/api/admin/session') return route.fulfill({ json: { authenticated: true, csrfToken: 'test-csrf' } });
      if (path.endsWith('/originals')) {
        listRequests++;
        if (failOriginals) return route.fulfill({ status: 503, json: { message: 'Originals unavailable. Try again.' } });
        return route.fulfill({ json: { expectedOriginals: 2, originals: Array.from({ length: available }, (_, i) => ({ id: `00${i + 1}.jpg`, previewUrl: `/api/admin/events/${eventId}/sessions/${hash}/originals/00${i + 1}.jpg` })) } });
      }
      if (path.includes('/originals/')) {
        imageRequests++;
        return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="160"><rect width="200" height="160" fill="#b4a7d6"/></svg>' });
      }
      if (path.endsWith('/deletion')) {
        mutations++;
        const body = route.request().postDataJSON();
        check(route.request().headers()['x-csrf-token'] === 'test-csrf', 'Deletion sends CSRF header');
        check(body.version === String(version), 'Deletion sends current version');
        deleted = body.deleted; version++;
        return route.fulfill({ json: { sessionId: hash, deleted } });
      }
      const include = (url.searchParams.get('filter') === 'deleted') === deleted;
      return route.fulfill({ json: { eventId, name: 'Deletion test event', version: String(version), hiddenCount: 0, nextCursor: null,
        sessions: include ? [{ sessionId: hash, hidden: false, capturedUtc: '2026-11-15T12:00:00Z', previewUrl: '/brand/background2.webp', deleteAfterUtc: deleted ? new Date(Date.now() + 7 * 86400000).toISOString() : null }] : [] } });
    });
    const guestToken = 'C'.repeat(42) + 'A';
    await context.route('**/api/guest/**', route => {
      const photo = id => ({ id, bytes: 1000, contentType: 'image/jpeg', fileName: id + '.jpg', previewUrl: '/brand/background2.webp', downloadUrl: '/brand/background2.webp' });
      return deleted ? route.fulfill({ status: 404, json: {} }) : route.fulfill({ json: { state: 'ready', eventId, expectedOriginals: 1, uploadedOriginals: 1, final: photo('final'), originals: [photo('001.jpg')] } });
    });
    await admin.goto(`${base}/admin/events/${eventId}/sessions`);
    await admin.getByRole('button', { name: 'Preview session 1', exact: true }).click();
    check(listRequests === 0 && imageRequests === 0, 'Opening Preview makes no originals requests');
    await admin.getByRole('button', { name: 'Show originals', exact: true }).click();
    await admin.waitForFunction(() => document.getElementById('session-preview-status').textContent === '2 of 2 originals available.');
    await admin.locator('#session-originals img').first().scrollIntoViewIfNeeded();
    check(listRequests === 1 && await admin.locator('#session-originals img').count() === 2, 'Explicit Show originals loads metadata and images');
    await admin.getByRole('button', { name: 'Hide originals', exact: true }).click();
    check(await admin.locator('#session-originals').isHidden() && await admin.locator('#session-originals img').count() === 0, 'Hide clears original images');
    await admin.keyboard.press('Escape');
    await admin.getByRole('button', { name: 'Preview session 1', exact: true }).click();
    check(await admin.getByRole('button', { name: 'Show originals', exact: true }).isVisible() && listRequests === 1, 'Reopening starts hidden without requesting originals');
    available = 0;
    await admin.getByRole('button', { name: 'Show originals', exact: true }).click();
    await admin.waitForFunction(() => document.getElementById('session-preview-status').textContent.startsWith('0 of 2'));
    await admin.getByRole('button', { name: 'Hide originals', exact: true }).click();
    available = 2; failOriginals = true;
    await admin.getByRole('button', { name: 'Show originals', exact: true }).click();
    await admin.waitForFunction(() => document.getElementById('session-preview-status').textContent.includes('Originals unavailable'));
    check(await admin.getByRole('button', { name: 'Show originals', exact: true }).isVisible(), 'Failed originals request allows retry');
    failOriginals = false;
    await admin.getByRole('button', { name: 'Show originals', exact: true }).click();
    await admin.waitForFunction(() => document.getElementById('session-preview-status').textContent === '2 of 2 originals available.');
    await admin.keyboard.press('Escape');
    const guest = await context.newPage(); await guest.goto(base + '/p/' + guestToken);
    await guest.locator('#originals .photo-card').waitFor();
    await guest.locator('#preview-final').click();
    await admin.getByRole('button', { name: 'Delete session', exact: true }).click();
    check(await admin.locator('#delete-session-confirm').evaluate(dialog => dialog.open) && mutations === 0, 'Delete requires confirmation before mutation');
    check((await admin.locator('#delete-session-detail').textContent()).includes('7 days'), 'Confirmation explains recovery period');
    await admin.getByRole('button', { name: 'Cancel', exact: true }).click();
    check(mutations === 0, 'Cancel leaves session untouched');
    await admin.getByRole('button', { name: 'Delete session', exact: true }).click();
    await admin.locator('#accept-session-delete').click();
    await admin.locator('#no-sessions').waitFor({ state: 'visible' });
    check(deleted && mutations === 1, 'Confirmed deletion removes card from active view');
    await guest.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    await guest.waitForFunction(() => document.getElementById('waiting-title').textContent === 'This session is unavailable.');
    check(await guest.locator('#final-section').isHidden() && await guest.locator('#originals .photo-card').count() === 0 &&
      !await guest.locator('#preview').evaluate(dialog => dialog.open), 'Deleted guest session clears loaded photos and Preview on refresh');
    await admin.locator('#session-filter').selectOption('deleted');
    await admin.getByRole('button', { name: 'Restore session', exact: true }).waitFor();
    check((await admin.locator('.managed-card-actions').textContent()).includes('hours to restore'), 'Deleted view shows recovery time');
    await admin.getByRole('button', { name: 'Preview session 1', exact: true }).click();
    check((await admin.locator('#session-preview-status').textContent()).includes('Guest access is disabled'), 'Deleted Preview explains guest access');
    check(await admin.getByRole('button', { name: 'Show originals', exact: true }).isVisible(), 'Deleted originals start hidden');
    await admin.keyboard.press('Escape');
    for (const width of [1440, 390, 320]) {
      await admin.setViewportSize({ width, height: 900 });
      check(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No layout overflow at ' + width);
      await admin.screenshot({ path: '/tmp/picme-deleted-sessions-' + width + '.png', fullPage: true });
    }
    await admin.getByRole('button', { name: 'Restore session', exact: true }).click();
    await admin.locator('#no-sessions').waitFor({ state: 'visible' });
    check(!deleted && mutations === 2, 'Restore removes card from Deleted view');
    await guest.locator('#refresh').click(); await guest.locator('#originals .photo-card').waitFor();
    check(await guest.locator('#final-section').isVisible(), 'Restore makes guest photos available again');
    await admin.locator('#session-filter').selectOption('all');
    await admin.getByRole('button', { name: 'Delete session', exact: true }).waitFor();
    check(errors.length === 0, 'No browser errors: ' + errors.join(', '));
    return { passed: ['no eager originals requests', 'on-demand originals', 'hide and reset on reopen', 'pending uploads and retry', 'confirmation and cancel', 'delete and restore', 'Deleted filter and recovery time', 'deleted Preview', 'guest access removal and restoration', 'desktop/mobile layouts'], errors };
  } finally { await context.close(); }
}
