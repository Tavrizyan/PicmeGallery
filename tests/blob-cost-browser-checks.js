// Run with Playwright browser_run_code against the local app on https://localhost:5079.
async (page) => {
  const context = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  try {
    const base = 'https://localhost:5079', token = 'A'.repeat(43), hash = 'a'.repeat(64), eventId = 'cost-test';
    const check = (value, label) => { if (!value) throw new Error(label); };
    const jpeg = await page.screenshot({ type: 'jpeg' });
    const gif = Buffer.from('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7', 'base64');
    let ownerReads = 0, fileReads = 0, failVariants = false;
    const errors = []; context.on('page', tab => tab.on('pageerror', error => errors.push(error.message)));
    const asset = (id = 'final') => ({ id, bytes: gif.length, contentType: id === 'final' ? 'image/gif' : 'image/jpeg', fileName: id + '.gif',
      thumbnailUrl: '/cost-images/thumb-' + id + '.jpg', previewUrl: '/cost-images/preview-' + id + '.gif',
      originalUrl: '/cost-images/original-' + id + '.gif', downloadUrl: '/cost-images/original-' + id + '.gif?download=true', fileUrl: '/cost-file' });
    await context.route('**/cost-images/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (failVariants && !path.includes('original-')) return route.fulfill({ status: 404 });
      return route.fulfill({ body: path.endsWith('.gif') ? gif : jpeg, contentType: path.endsWith('.gif') ? 'image/gif' : 'image/jpeg' });
    });
    await context.route('**/cost-file', route => { fileReads++; return route.fulfill({ body: gif, contentType: 'image/gif' }); });
    await context.route('**/api/guest/**', route => route.fulfill({ json: { state: 'ready', eventId, expectedOriginals: 1, uploadedOriginals: 1, final: asset(), originals: [asset('001.jpg')] } }));
    await context.route('**/api/event/**', route => {
      ownerReads++;
      return route.fulfill({ json: { name: 'Cost test', visibilityVersion: 'v1', sessions: [{ sessionId: hash, final: asset(), sessionPath: `/e/${token}/s/${hash}` }], nextCursor: null, downloadAllUrl: '#' } });
    });
    const html = await (await page.request.get(base + '/admin-sessions.html')).body();
    await context.route('**/admin/events/*/sessions', route => route.fulfill({ body: html, contentType: 'text/html' }));
    await context.route('**/api/admin/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/session')) return route.fulfill({ json: { authenticated: true, csrfToken: 'test' } });
      if (path.endsWith('/originals')) return route.fulfill({ json: { expectedOriginals: 1, originals: [asset('001.jpg')] } });
      const photo = asset();
      return route.fulfill({ json: { name: 'Cost test', eventId, version: 'v1', hiddenCount: 0, sessions: [{ sessionId: hash, hidden: false, ...photo }], nextCursor: null } });
    });
    const owner = await context.newPage();
    await owner.clock.install();
    await owner.goto(base + '/e/' + token);
    await owner.locator('#event-photos .photo-card').waitFor();
    check((await owner.locator('#event-photos .photo-preview img').getAttribute('src')).includes('thumb-'), 'Owner grid uses still thumbnails');
    await owner.clock.runFor(180000);
    check(ownerReads === 1, 'Visible owner gallery does not rescan on a timer');
    await owner.locator('#refresh-event').click();
    await owner.waitForFunction(() => !document.getElementById('refresh-event').disabled);
    check(ownerReads === 2, 'Manual Refresh still updates owner gallery');
    await owner.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    await owner.waitForFunction(() => !document.getElementById('refresh-event').disabled);
    check(ownerReads === 3, 'Returning to the tab still refreshes');
    await owner.locator('.photo-preview').click();
    check((await owner.locator('#event-preview-image').getAttribute('src')).includes('preview-final.gif'), 'Owner Preview uses reduced animated image');
    await owner.keyboard.press('Escape');
    const guest = await context.newPage(); await guest.goto(base + '/p/' + token);
    await guest.locator('#originals .photo-card').waitFor();
    check((await guest.locator('#originals .photo-preview img').getAttribute('src')).includes('thumb-'), 'Guest grid uses small images');
    await guest.locator('#preview-final').click();
    check((await guest.locator('#preview-image').getAttribute('src')).includes('preview-') && fileReads === 0, 'Guest Preview does not fetch full original for sharing');
    await guest.keyboard.press('Escape');
    const admin = await context.newPage(); await admin.goto(`${base}/admin/events/${eventId}/sessions`);
    await admin.locator('#managed-sessions .photo-card').waitFor();
    check((await admin.locator('#managed-sessions .photo-preview img').getAttribute('src')).includes('thumb-'), 'Admin grid uses small images');
    await admin.locator('.photo-preview').click(); await admin.locator('#show-session-originals').click();
    await admin.locator('#session-originals img').waitFor();
    check((await admin.locator('#session-originals img').getAttribute('src')).includes('preview-001.jpg'), 'Admin originals load reduced previews only after explicit click');
    await admin.keyboard.press('Escape');
    failVariants = true;
    await guest.reload();
    await guest.waitForFunction(() => document.getElementById('final-image').getAttribute('src').includes('original-') && document.querySelector('#originals .photo-preview img')?.getAttribute('src').includes('original-'));
    await guest.locator('#preview-final').click();
    await guest.waitForFunction(() => document.getElementById('preview-image').getAttribute('src').includes('original-'));
    await owner.reload();
    await owner.waitForFunction(() => document.querySelector('#event-photos .photo-preview img')?.getAttribute('src').includes('original-'));
    await owner.locator('.photo-preview').click();
    await owner.waitForFunction(() => document.getElementById('event-preview-image').getAttribute('src').includes('original-'));
    await admin.reload();
    await admin.waitForFunction(() => document.querySelector('#managed-sessions .photo-preview img')?.getAttribute('src').includes('original-'));
    await admin.locator('.photo-preview').click(); await admin.locator('#show-session-originals').click();
    await admin.waitForFunction(() => document.getElementById('session-preview-image').getAttribute('src').includes('original-') && document.querySelector('#session-originals img')?.getAttribute('src').includes('original-'));
    check(errors.length === 0, 'No browser script errors: ' + errors.join(', '));
    return { passed: ['no timed owner scans', 'manual and return-to-tab refresh', 'small guest/owner/admin browsing images', 'animated reduced Preview', 'no eager original sharing fetch', 'original fallback for missing derivatives in all viewers'], errors };
  } finally { await context.close(); }
}
