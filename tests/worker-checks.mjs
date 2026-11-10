import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../cloudflare/worker.js', import.meta.url), 'utf8');
const { default: worker } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
test('proxy preserves guest path/query, uses Azure origin, and does not follow download redirects', async () => {
  const saved = globalThis.fetch;
  try {
    let request;
    globalThis.fetch = async (url, options) => { request = { url: new URL(url), options }; return new Response(null, {status:302,headers:{Location:'https://account.blob.core.windows.net/photos/final.jpg?sig=example'}}); };
    const response = await worker.fetch(new Request('https://photos.picme.am/p/example?test=1'), { AZURE_ORIGIN:'https://gallery.azurewebsites.net/' });
    assert.equal(request.url.origin, 'https://gallery.azurewebsites.net');
    assert.equal(request.url.pathname, '/p/example'); assert.equal(request.url.search, '?test=1');
    assert.equal(request.options.redirect, 'manual');
    assert.equal(response.headers.get('Location'), 'https://account.blob.core.windows.net/photos/final.jpg?sig=example');
    assert.equal(response.headers.get('Cache-Control'), 'private, no-store');
  } finally { globalThis.fetch = saved; }
});
test('double-slash paths cannot change the configured origin', async () => {
  const saved = globalThis.fetch;
  try {
    let target;
    globalThis.fetch = async url => { target = new URL(url); return new Response('ok'); };
    await worker.fetch(new Request('https://photos.picme.am//attacker.example/path'), {AZURE_ORIGIN:'https://gallery.azurewebsites.net/'});
    assert.equal(target.hostname, 'gallery.azurewebsites.net');
  } finally { globalThis.fetch = saved; }
});
test('same-origin redirects remain on the public hostname', async () => {
  const saved = globalThis.fetch;
  try {
    globalThis.fetch = async () => new Response(null, {status:302, headers:{Location:'https://gallery.azurewebsites.net/p/example'}});
    const response = await worker.fetch(new Request('https://photos.picme.am/'), {AZURE_ORIGIN:'https://gallery.azurewebsites.net/'});
    assert.equal(response.headers.get('Location'), 'https://photos.picme.am/p/example');
  } finally { globalThis.fetch = saved; }
});
test('invalid or missing Azure origins fail closed', async () => {
  for (const origin of [undefined, 'http://gallery.azurewebsites.net/', 'https://attacker.example/', 'https://gallery.azurewebsites.net/?evil=1'])
    assert.equal((await worker.fetch(new Request('https://photos.picme.am/'), {AZURE_ORIGIN:origin})).status, 503);
});
