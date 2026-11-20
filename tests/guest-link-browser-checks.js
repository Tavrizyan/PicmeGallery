// Playwright function; run from the repository root so tests/fixtures is available.
async (page) => {
  const { readFile } = await import('node:fs/promises');
  const { createHash } = await import('node:crypto');
  const jpeg = await readFile('tests/fixtures/guest-link-final.jpg');
  const gif = await readFile('tests/fixtures/guest-link-final.gif');
  const patterned = await readFile('tests/fixtures/guest-link-patterned-final.jpg');
  const wrong = await readFile('tests/fixtures/guest-link-wrong-session.png');
  const context = await page.context().browser().newContext();
  try {
    const base = 'https://localhost:5079', ownerToken = 'B'.repeat(42) + 'A', guestToken = 'A'.repeat(43);
    const sessionId = createHash('sha256').update(guestToken).digest('hex'), guestUrl = 'https://photos.picme.am/p/' + guestToken;
    const check = (value, label) => { if (!value) throw new Error(label); };
    let mode = 'jpeg', fileReads = 0, workerReads = 0, decoderReads = 0, denied = false;
    const errors = [];
    context.on('page', tab => tab.on('pageerror', error => errors.push(error.message)));
    context.on('request', request => {
      if (request.url().endsWith('/guest-link-worker.js')) workerReads++;
      if (request.url().endsWith('/vendor/jsqr/jsQR.js')) decoderReads++;
    });
    await context.addInitScript(() => Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
      writeText: async text => { if (window.blockClipboard) throw new DOMException('Blocked', 'NotAllowedError'); window.copiedGuestLink = text; }
    } }));
    const asset = () => ({ id: 'final', bytes: mode === 'gif' ? gif.length : jpeg.length,
      contentType: mode === 'gif' ? 'image/gif' : 'image/jpeg', fileName: mode === 'gif' ? 'picme-final.gif' : 'picme-final.jpg',
      previewUrl: '/brand/background2.webp', fileUrl: `/api/event/${ownerToken}/media/${sessionId}/final?file=true`, downloadUrl: '#' });
    await context.route('**/api/event/**', async route => {
      const url = new URL(route.request().url());
      if (denied) return route.fulfill({ status: 404, json: {} });
      if (url.pathname.includes('/media/')) {
        fileReads++;
        if (mode === 'fail') return route.fulfill({ status: 503 });
        if (mode === 'delay') await new Promise(resolve => setTimeout(resolve, 1000));
        return route.fulfill({ body: mode === 'gif' ? gif : mode === 'wrong' ? wrong : mode === 'patterned' ? patterned : jpeg,
          contentType: mode === 'gif' ? 'image/gif' : mode === 'wrong' ? 'image/png' : 'image/jpeg' });
      }
      if (url.pathname.includes('/sessions/')) return route.fulfill({ json: { state: 'ready', eventId: 'share-test', expectedOriginals: 1, uploadedOriginals: 1, final: asset(), originals: [] } });
      return route.fulfill({ json: { name: 'Share test', visibilityVersion: 'v1', sessions: [{ sessionId, final: asset(), sessionPath: `/e/${ownerToken}/s/${sessionId}` }], nextCursor: null, downloadAllUrl: '#' } });
    });
    await context.route('**/api/guest/**', route => route.fulfill({ json: { state: 'ready', expectedOriginals: 1, uploadedOriginals: 1, final: asset(), originals: [] } }));
    const owner = await context.newPage();
    await owner.goto(base + '/e/' + ownerToken); await owner.locator('.photo-preview').click();
    check(fileReads === 0 && workerReads === 0 && decoderReads === 0, 'Opening Preview does not fetch originals or the QR decoder');
    await owner.locator('#preview-guest-link').click();
    await owner.locator('#guest-link-url').waitFor({ state: 'visible' });
    check(await owner.locator('#guest-link-url').inputValue() === guestUrl, 'Real JPEG scan skips static QR and finds the exact matching guest URL');
    check(fileReads === 1 && workerReads === 1 && decoderReads === 1, 'QR decoder and original load only on explicit request');
    await owner.locator('#copy-guest-url').click();
    check(await owner.evaluate(() => window.copiedGuestLink) === guestUrl, 'Copy shares the guest token rather than the event owner token');
    check(await owner.locator('#open-guest-url').getAttribute('href') === guestUrl, 'Open targets the exact printed URL');
    await owner.keyboard.press('Escape');
    check(await owner.evaluate(() => document.activeElement.id) === 'preview-guest-link' && await owner.locator('#event-preview').evaluate(dialog => dialog.open), 'Closing guest dialog restores focus inside the final Preview');
    await owner.locator('#preview-guest-link').click();
    check(await owner.locator('#guest-link-url').inputValue() === guestUrl && fileReads === 1, 'Repeated opening reuses the in-memory result');
    await owner.evaluate(() => { window.blockClipboard = true; }); await owner.locator('#copy-guest-url').click();
    check((await owner.locator('#guest-link-status').textContent()).includes('Select and copy') &&
      await owner.locator('#guest-link-url').evaluate(input => input.selectionEnd - input.selectionStart === input.value.length), 'Blocked clipboard offers manual copying');
    for (const width of [1440, 390, 320]) {
      await owner.setViewportSize({ width, height: 900 });
      check(await owner.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No guest-link layout overflow at ' + width);
      await owner.screenshot({ path: '/tmp/picme-guest-link-' + width + '.png' });
    }
    await owner.keyboard.press('Escape'); await owner.keyboard.press('Escape');
    const session = await context.newPage(); await session.goto(`${base}/e/${ownerToken}/s/${sessionId}`);
    await session.locator('#session-guest-link').click(); await session.locator('#guest-link-url').waitFor({ state: 'visible' });
    check(await session.locator('#guest-link-url').inputValue() === guestUrl, 'Owner session page also scans its final');
    await session.keyboard.press('Escape');
    const guest = await context.newPage(); await guest.goto(base + '/p/' + guestToken);
    check(await guest.locator('#session-guest-link').isHidden(), 'Guest pages do not expose the owner action');
    mode = 'gif'; await owner.reload(); await owner.locator('.photo-preview').click(); await owner.locator('#preview-guest-link').click();
    await owner.locator('#guest-link-url').waitFor({ state: 'visible' });
    check(await owner.locator('#guest-link-url').inputValue() === guestUrl, 'Real animated GIF scan returns the same printed URL');
    mode = 'patterned'; await owner.reload(); await owner.locator('.photo-preview').click(); await owner.locator('#preview-guest-link').click();
    await owner.locator('#guest-link-url').waitFor({ state: 'visible' });
    check(await owner.locator('#guest-link-url').inputValue() === guestUrl, 'Small inverted guest QR on patterned artwork is matched after skipping another session QR');
    mode = 'wrong'; await owner.reload(); await owner.locator('.photo-preview').click(); await owner.locator('#preview-guest-link').click();
    await owner.locator('#retry-guest-url').waitFor({ state: 'visible' });
    check((await owner.locator('#guest-link-status').textContent()).includes('No matching guest QR') &&
      await owner.locator('#guest-link-url').isHidden() && await owner.locator('#copy-guest-url').isDisabled(), 'A different session QR is never offered for sharing');
    mode = 'jpeg'; await owner.locator('#retry-guest-url').click(); await owner.locator('#guest-link-url').waitFor({ state: 'visible' });
    mode = 'fail'; await owner.reload(); await owner.locator('.photo-preview').click(); await owner.locator('#preview-guest-link').click();
    await owner.locator('#retry-guest-url').waitFor({ state: 'visible' });
    check((await owner.locator('#guest-link-status').textContent()).includes('unavailable'), 'Failed original fetch offers retry');
    await owner.keyboard.press('Escape'); await owner.keyboard.press('Escape');
    mode = 'delay'; await owner.reload(); await owner.locator('.photo-preview').click();
    const before = fileReads; await owner.locator('#preview-guest-link').click();
    await owner.waitForFunction(() => document.getElementById('guest-link-status').textContent.includes('Reading'));
    await owner.keyboard.press('Escape');
    check(!await owner.locator('.guest-link-dialog').evaluate(dialog => dialog.open), 'Closing cancels a pending scan');
    mode = 'jpeg'; await owner.locator('#preview-guest-link').click(); await owner.locator('#guest-link-url').waitFor({ state: 'visible' });
    check(fileReads >= before + 1 && await owner.locator('#guest-link-url').inputValue() === guestUrl, 'Canceled scans can be reopened safely');
    denied = true; await owner.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    await owner.waitForFunction(() => document.getElementById('event-title').textContent === 'Link unavailable');
    check(!await owner.locator('.guest-link-dialog').evaluate(dialog => dialog.open), 'Revoked owner access closes the guest-link dialog');
    check(errors.length === 0, 'No script errors: ' + errors.join(', '));
    return { passed: ['on-demand original/decoder loading', 'real JPEG with multiple QRs', 'small/inverted QR on patterned artwork', 'animated GIF', 'session-hash validation', 'exact URL copy/open', 'clipboard fallback', 'memory reuse', 'owner session action', 'guest action hidden', 'retry/cancel/reopen', 'revoked access cleanup', 'responsive dialog/focus'], errors };
  } finally { await context.close(); }
}
