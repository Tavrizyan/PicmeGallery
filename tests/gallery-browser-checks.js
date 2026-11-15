// Run this function with the Playwright browser_run_code tool (filename), against the local app on :5078.
// Native OS share sheets still require a real iPhone/Android device check.
async (page) => {
  const context = await page.context().browser().newContext();
  page = await context.newPage();
  try {
    const check = (value, label) => { if (!value) throw new Error(label); };
    const token = 'A'.repeat(43), base = 'http://localhost:5078';
    const jpeg = await page.screenshot({ type: 'jpeg' });
    const gif = Buffer.from('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7', 'base64');
    let state = 'ready', originals = ['003.jpg', '001.jpg', '002.jpg'], gifFinal = false, apiError = false, fileError = false;
    const asset = id => ({
      id, bytes: jpeg.length, contentType: id === 'final' && gifFinal ? 'image/gif' : 'image/jpeg',
      fileName: id === 'final' ? `picme-final.${gifFinal ? 'gif' : 'jpg'}` : `picme-original-${id}`,
      previewUrl: `/api/media/${token}/${id}`, downloadUrl: `/api/media/${token}/${id}?download=true`, fileUrl: `/api/media/${token}/${id}?file=true`
    });
    await page.route('**/api/guest/**', route => apiError
      ? route.fulfill({ status: 503, json: { message: 'Unavailable' } })
      : route.fulfill({ json: { state, expectedOriginals: 3, uploadedOriginals: originals.length,
        final: state === 'waiting' ? null : asset('final'), originals: state === 'waiting' ? [] : originals.map(asset) } }));
    await page.route('**/api/media/**', route => {
      if (fileError && route.request().url().includes('file=true')) return route.fulfill({ status: 503 });
      const isGif = gifFinal && new URL(route.request().url()).pathname.endsWith('/final');
      return route.fulfill({ body: isGif ? gif : jpeg, contentType: isGif ? 'image/gif' : 'image/jpeg' });
    });
    await page.addInitScript(() => {
      const platform = new URLSearchParams(location.search).get('platform');
      if (!platform) return;
      Object.defineProperty(navigator, 'userAgent', { get: () => platform === 'ios' ? 'iPhone' : 'Android', configurable: true });
      Object.defineProperty(navigator, 'canShare', { value: data => !window.unsupportedShare && !!data.files?.[0], configurable: true });
      Object.defineProperty(navigator, 'share', { value: async data => {
        if (window.shareMode === 'cancel') throw new DOMException('Cancelled', 'AbortError');
        if (window.shareMode === 'error') throw new DOMException('Unavailable', 'NotAllowedError');
        window.sharedFile = { name: data.files[0].name, type: data.files[0].type, size: data.files[0].size, active: navigator.userActivation.isActive };
      }, configurable: true });
    });
    const open = platform => page.goto(`${base}/p/${token}${platform ? '?platform=' + platform : ''}`);
    const readyToShare = () => page.waitForFunction(() => {
      const button = document.getElementById('share-photo'); return !button.hidden && !button.disabled;
    });
    const results = [];
    await page.setViewportSize({ width: 1440, height: 1000 });
    await open();
    await page.locator('.photo-card').nth(2).waitFor();
    check((await page.locator('.photo-card').evaluateAll(cards => cards.map(card => card.dataset.photoId))).join() === '001.jpg,002.jpg,003.jpg', 'Photos sorted');
    await page.locator('#preview-final').click();
    await page.locator('#preview-image').evaluate(image => {
      const start = new Event('touchstart'), end = new Event('touchend');
      Object.defineProperty(start, 'touches', { value: [{ clientX: 220, clientY: 150 }] });
      Object.defineProperty(end, 'changedTouches', { value: [{ clientX: 80, clientY: 160 }] });
      image.dispatchEvent(start); image.dispatchEvent(end);
    });
    check(await page.locator('#preview-title').textContent() === 'Original photo 1', 'Swipe changes photo');
    await page.locator('#previous-photo').click();
    check(await page.locator('#preview-title').textContent() === 'Your finished print', 'Previous button');
    await page.keyboard.press('Tab');
    check(await page.evaluate(() => document.getElementById('preview').contains(document.activeElement)), 'Modal contains focus');
    await page.keyboard.press('ArrowRight');
    check(await page.locator('#preview-title').textContent() === 'Original photo 1', 'Next image via keyboard');
    await page.keyboard.press('ArrowLeft');
    check(await page.locator('#preview-title').textContent() === 'Your finished print', 'Previous image via keyboard');
    await page.keyboard.press('Escape');
    check(await page.evaluate(() => !document.getElementById('preview').open && document.activeElement.id === 'preview-final'), 'Close and restore focus');
    await page.locator('#refresh').click();
    await page.waitForFunction(() => !document.getElementById('refresh').disabled);
    check(await page.locator('.photo-card').count() === 3, 'Refresh without duplicates');
    await page.screenshot({ path: '/tmp/picme-gallery-desktop.png', fullPage: true });
    results.push('desktop preview, swipe, sorting, keyboard, focus, refresh');

    for (const platform of ['ios', 'android']) {
      await page.setViewportSize({ width: platform === 'ios' ? 390 : 360, height: 844 });
      await open(platform);
      await page.locator('#save-final').click();
      await readyToShare();
      await page.locator('#share-photo').click();
      const shared = await page.evaluate(() => window.sharedFile);
      check(shared.active && shared.name === 'picme-final.jpg' && shared.type === 'image/jpeg' && shared.size === jpeg.length, 'Original file shared with transient activation');
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No mobile overflow');
      await page.screenshot({ path: `/tmp/picme-gallery-${platform}-preview.png` });
      await page.locator('#close-preview').click();
      await page.screenshot({ path: `/tmp/picme-gallery-${platform}.png`, fullPage: true });
      await page.evaluate(() => { window.shareMode = 'cancel'; });
      await page.locator('.save-small').first().click();
      await readyToShare();
      await page.locator('#share-photo').click();
      check(await page.locator('#save-status').textContent() === '', 'Cancellation is silent');
      await page.evaluate(() => { window.shareMode = 'error'; });
      await page.locator('#share-photo').click();
      check((await page.locator('#save-status').textContent()).includes('Download'), 'Share failure offers fallback');
      await page.locator('#close-preview').click();
      await page.evaluate(() => { window.unsupportedShare = true; });
      await page.locator('#save-final').click();
      check(await page.locator('#share-photo').isHidden() && await page.locator('#download-photo').isVisible(), 'Unsupported sharing retains download');
      await page.locator('#close-preview').click();
      results.push(`${platform}: responsive layout, file share activation, cancellation, blocked/unsupported sharing`);
    }
    fileError = true;
    await open('ios');
    await page.locator('#save-final').click();
    await page.waitForFunction(() => document.getElementById('save-status').textContent.includes('couldn’t be prepared'));
    check(await page.locator('#share-photo').isHidden(), 'Failed preparation hides share');
    await page.locator('#close-preview').click();
    fileError = false; gifFinal = true;
    await open('ios');
    await page.locator('#save-final').click();
    await readyToShare();
    await page.locator('#share-photo').click();
    check(await page.evaluate(() => window.sharedFile.type === 'image/gif' && window.sharedFile.name === 'picme-final.gif'), 'GIF preserved for native sharing');
    await page.locator('#close-preview').click();
    gifFinal = false; state = 'waiting'; originals = [];
    await open();
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('checks automatically'));
    check(await page.locator('#waiting').isVisible() && await page.locator('#final-section').isHidden(), 'Waiting state');
    state = 'uploading'; originals = ['001.jpg'];
    await page.locator('#refresh').click();
    await page.locator('.photo-card').waitFor();
    check(await page.locator('#waiting').isHidden(), 'Partial uploads show final');
    apiError = true;
    await page.locator('#refresh').click();
    await page.waitForFunction(() => document.getElementById('status').classList.contains('error'));
    check(await page.locator('#refresh').isEnabled(), 'API failure allows retry');
    apiError = false; state = 'ready'; originals = ['001.jpg', '002.jpg', '003.jpg'];
    await page.locator('#refresh').click();
    await page.locator('.photo-card').nth(2).waitFor();
    check(!(await page.locator('#status').getAttribute('class'))?.includes('error'), 'Retry recovers');
    results.push('file failure fallback, GIF sharing, waiting/partial states, API error recovery');
    await page.unroute('**/api/guest/**'); await page.unroute('**/api/media/**');
    await page.goto(base);
    return { passed: results };
  } finally { await context.close(); }
}
