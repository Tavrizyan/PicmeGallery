// Run through the Playwright browser_run_code tool against the HTTPS local app on port 5079.
async (page) => {
  const context = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  try {
    const admin = await context.newPage(), base = 'https://localhost:5079', token = 'A'.repeat(43), guestToken = 'C'.repeat(42) + 'A';
    const first = 'a'.repeat(64), second = 'b'.repeat(64), eventId = 'visibility-test';
    const html = await (await admin.request.get(base + '/admin-sessions.html')).body();
    let version = 1, forceConflict = false;
    const hidden = new Set(), check = (value, label) => { if (!value) throw new Error(label); };
    const asset = (hash, id = 'final') => ({ id, bytes: 1000, contentType: 'image/jpeg', fileName: id + '.jpg', capturedUtc: '2026-11-15T12:00:00Z',
      previewUrl: '/brand/background2.webp', downloadUrl: `/api/event/${token}/media/${hash}/${id}?download=true`, fileUrl: `/api/event/${token}/media/${hash}/${id}?file=true` });
    const ownerSession = hash => ({ sessionId: hash, final: asset(hash), sessionPath: `/e/${token}/s/${hash}` });
    const gallery = hash => ({ state: 'ready', eventId, expectedOriginals: 2, uploadedOriginals: 2, final: asset(hash), originals: [asset(hash, '001.jpg'), asset(hash, '002.jpg')] });
    const pageOf = (query, filter) => {
      let remaining = query.has('cursor') ? [second] : [first, second];
      remaining = remaining.filter(filter);
      return { hashes: remaining.slice(0, 1), nextCursor: remaining.length > 1 ? 'second-page' : null };
    };
    await context.route('**/admin/events/*/sessions', route => route.fulfill({ body: html, contentType: 'text/html' }));
    await context.route('**/api/admin/**', async route => {
      const url = new URL(route.request().url()), path = url.pathname, query = url.searchParams;
      if (path === '/api/admin/session') return route.fulfill({ json: { authenticated: true, username: 'admin', csrfToken: 'test-csrf' } });
      if (path === '/api/admin/events') return route.fulfill({ json: { events: [{ eventId, name: 'Visibility test event', createdUtc: '2026-11-15T12:00:00Z', active: true, ownerPath: '/e/' + token, version: String(version) }], nextCursor: null } });
      if (path.endsWith('/visibility')) {
        const body = route.request().postDataJSON(), hash = path.split('/').at(-2);
        check(route.request().headers()['x-csrf-token'] === 'test-csrf', 'Visibility sends CSRF header');
        if (forceConflict) {
          forceConflict = false; hidden.add(hash); version++;
          return route.fulfill({ status: 409, json: { message: 'The event was changed in another tab. Sessions refreshed; try again.' } });
        }
        if (body.version !== String(version)) return route.fulfill({ status: 409, json: { message: 'Stale version' } });
        if (body.hidden) hidden.add(hash); else hidden.delete(hash); version++;
        return route.fulfill({ json: { sessionId: hash, hidden: body.hidden } });
      }
      const filter = query.get('filter') || 'all';
      const result = pageOf(query, hash => filter === 'all' || hidden.has(hash) === (filter === 'hidden'));
      return route.fulfill({ json: { eventId, name: 'Visibility test event', version: String(version), hiddenCount: hidden.size, nextCursor: result.nextCursor,
        sessions: result.hashes.map(hash => ({ sessionId: hash, hidden: hidden.has(hash), capturedUtc: '2026-11-15T12:00:00Z', contentType: 'image/jpeg', previewUrl: '/brand/background2.webp' })) } });
    });
    await context.route('**/api/event/**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.includes('/sessions/')) {
        const hash = url.pathname.split('/').at(-1);
        return hidden.has(hash) ? route.fulfill({ status: 404, json: { message: 'Unavailable' } }) : route.fulfill({ json: gallery(hash) });
      }
      const result = pageOf(url.searchParams, hash => !hidden.has(hash));
      return route.fulfill({ json: { name: 'Visibility test event', visibilityVersion: String(version), sessions: result.hashes.map(ownerSession), nextCursor: result.nextCursor, downloadAllUrl: `/api/event/${token}/download` } });
    });
    await context.route('**/api/guest/**', route => route.fulfill({ json: gallery(first) }));
    const errors = [];
    context.on('page', tab => tab.on('pageerror', error => errors.push(error.message)));
    admin.on('pageerror', error => errors.push(error.message));
    await admin.goto(base + '/admin/login');
    await admin.getByRole('link', { name: 'Manage sessions', exact: true }).click();
    await admin.locator('#managed-sessions .photo-card').waitFor();
    await admin.locator('#more-sessions').click(); await admin.locator('#managed-sessions .photo-card').nth(1).waitFor();
    await admin.getByRole('button', { name: 'Preview session 1', exact: true }).click();
    await admin.locator('#session-preview[open]').waitFor(); await admin.keyboard.press('Escape');
    check(await admin.evaluate(() => document.activeElement.getAttribute('aria-label') === 'Preview session 1'), 'Admin preview restores keyboard focus');
    const owner = await context.newPage(); await owner.goto(base + '/e/' + token);
    await owner.locator('#event-photos .photo-card').waitFor();
    const session = await context.newPage(); await session.goto(base + `/e/${token}/s/${first}`);
    await session.locator('#originals .photo-card').nth(1).waitFor();
    await session.locator('#preview-final').click();
    await admin.getByRole('button', { name: 'Hide session 1 from event owner', exact: true }).click();
    await admin.waitForFunction(() => document.querySelector('#managed-sessions .hidden-session') && document.getElementById('sessions-status').textContent.includes('guest QR still works'));
    check(hidden.has(first) && await admin.locator('#hidden-count').textContent() === '1 session hidden', 'Hide persists and updates status');
    await admin.getByRole('button', { name: 'Preview session 1', exact: true }).click();
    check(await admin.locator('#session-preview-title').textContent() === 'Hidden from event owner', 'Admin can preview hidden sessions');
    await admin.keyboard.press('Escape');
    await owner.locator('#more-photos').click();
    await owner.waitForFunction(hash => document.querySelectorAll('#event-photos .photo-card').length === 1 && !document.querySelector(`#event-photos a[href$="${hash}"]`), first);
    check(await owner.getByRole('link', { name: 'All photos for moment 1', exact: true }).getAttribute('href') === `/e/${token}/s/${second}`, 'Owner continuation restarts from the first page when visibility changes');
    await session.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    await session.waitForFunction(() => document.getElementById('waiting-title').textContent === 'This session is unavailable.');
    check(await session.locator('#final-section').isHidden() && await session.locator('#originals .photo-card').count() === 0 &&
      !await session.locator('#preview').evaluate(dialog => dialog.open), 'Open owner session clears photos and prepared preview after hiding');
    const guest = await context.newPage(); await guest.goto(base + '/p/' + guestToken);
    await guest.locator('#originals .photo-card').nth(1).waitFor();
    check(await guest.locator('#final-section').isVisible() && await guest.locator('#guest-event-id').textContent() === eventId, 'Guest QR gallery still shows hidden-session photos');
    await admin.locator('#session-filter').selectOption('visible');
    await admin.waitForFunction(hash => document.querySelector('#managed-sessions .photo-card')?.dataset.sessionId === hash, second);
    check(await admin.locator('.hidden-session').count() === 0, 'Visible filter excludes hidden sessions');
    await admin.locator('#session-filter').selectOption('hidden');
    await admin.locator('.hidden-session').waitFor();
    await admin.getByRole('button', { name: 'Restore session 1 for event owner', exact: true }).click();
    await admin.locator('#no-sessions').waitFor({ state: 'visible' });
    check(!hidden.has(first) && await admin.locator('#hidden-count').textContent() === '0 sessions hidden', 'Restore removes session from hidden-only view');
    await owner.locator('#refresh-event').click(); await owner.locator('#event-photos .photo-card').nth(1).waitFor();
    await session.locator('#refresh').click(); await session.locator('#originals .photo-card').nth(1).waitFor();
    check(await session.locator('#final-section').isVisible(), 'Restored owner session loads again');
    await admin.locator('#session-filter').selectOption('all');
    await admin.locator('#managed-sessions .photo-card').waitFor(); forceConflict = true;
    await admin.getByRole('button', { name: 'Hide session 1 from event owner', exact: true }).click();
    await admin.waitForFunction(() => document.getElementById('sessions-status').textContent.includes('another tab'));
    check(await admin.locator('#managed-sessions .hidden-session').count() === 1 && await admin.locator('#session-filter').isEnabled(), 'Conflicting edit refreshes server state and leaves controls available');
    await admin.locator('#more-sessions').click(); await admin.locator('#managed-sessions .photo-card').nth(1).waitFor();
    for (const width of [1440, 390, 320]) {
      await admin.setViewportSize({ width, height: 900 });
      check(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No management layout overflow at ' + width);
      await admin.screenshot({ path: '/tmp/picme-session-management-' + width + '.png', fullPage: true });
    }
    check(errors.length === 0, 'No script errors: ' + errors.join(', '));
    return { passed: ['Manage sessions navigation', 'admin preview/focus', 'hide and restore', 'All/Visible/Hidden filters', 'owner loaded-card reconciliation', 'open owner session access removal and restoration', 'guest QR remains available', 'stale-edit recovery', 'desktop/mobile responsive layouts'], errors };
  } finally { await context.close(); }
}
