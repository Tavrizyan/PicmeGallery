// Run through the Playwright browser_run_code tool with the local HTTPS app on port 5079.
async (page) => {
  const context = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  try {
    const tab = await context.newPage(), errors = [];
    const check = (value, label) => { if (!value) throw new Error(label); };
    let requests = 0, releaseFirst;
    const firstRequest = new Promise(resolve => { releaseFirst = resolve; });
    await tab.route('**/api/admin/session', route => route.fulfill({ json: { authenticated: true, username: 'admin', csrfToken: 'test-csrf' } }));
    await tab.route('**/api/admin/events', async route => {
      requests++;
      if (requests === 1) {
        await firstRequest;
        return route.fulfill({ status: 503, json: { message: 'Event list temporarily unavailable.' } });
      }
      return route.fulfill({ json: { events: [{ eventId: 'saved-event', name: 'Already created event', createdUtc: '2026-11-15T18:00:00Z',
        active: true, ownerPath: '/e/' + 'A'.repeat(43), version: '1' }], nextCursor: null } });
    });
    tab.on('pageerror', error => errors.push(error.message));
    await tab.goto('https://localhost:5079/admin/login');
    await tab.waitForFunction(() => document.getElementById('admin-status').textContent === 'Loading your events…');
    check(await tab.locator('#events-panel').isVisible(), 'Authenticated controls appear while the event request is pending');
    releaseFirst();
    await tab.locator('#retry-admin').waitFor({ state: 'visible' });
    check(await tab.locator('#admin-status').textContent() === 'Event list temporarily unavailable.', 'Startup failure replaces Opening administration with an actionable error');
    await tab.locator('#retry-admin').click();
    await tab.locator('.event-row').waitFor();
    check(requests === 2 && await tab.locator('.event-row').count() === 1, 'Retry renders exactly one previously saved event row');
    check(await tab.locator('.event-row h2').textContent() === 'Already created event', 'Saved event data is displayed');
    check(await tab.locator('#admin-status').textContent() === '' && await tab.locator('#retry-admin').isHidden(), 'Successful startup clears loading and retry state');
    check(await tab.getByRole('link', { name: 'Manage sessions', exact: true }).isVisible() && await tab.getByRole('button', { name: 'Copy link', exact: true }).isVisible(), 'Existing owner-link controls remain available');
    check(errors.length === 0, 'No startup script errors: ' + errors.join(', '));
    return { passed: ['pending request status', 'visible authenticated controls', 'startup error and retry', 'one existing event row', 'owner-link controls'], errors };
  } finally { await context.close(); }
}
