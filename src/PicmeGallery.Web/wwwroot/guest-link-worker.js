"use strict";
importScripts("/vendor/jsqr/jsQR.js");

async function matchesSession(value, sessionId) {
  if (value.length > 2048 || /\s/.test(value)) return false;
  let url;
  try { url = new URL(value); } catch { return false; }
  if (url.protocol !== "https:" && !(url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname))) return false;
  if (url.username || url.password || url.search || url.hash) return false;
  const token = /^\/p\/([A-Za-z0-9_-]{43})\/?$/.exec(url.pathname)?.[1];
  if (!token) return false;
  const bytes = atob(token.replace(/-/g, "+").replace(/_/g, "/") + "=");
  if (bytes.length !== 32 || btoa(bytes).replace(/=/g, "").replace(/\+/g, "-").replace(/\//g, "_") !== token) return false;
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(token));
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, "0")).join("") === sessionId;
}

self.onmessage = async ({ data }) => {
  try {
    const pixels = new Uint8ClampedArray(data.pixels);
    for (let pass = 0; pass < 2; pass++) {
      if (pass === 1) {
        // Coloured artwork behind transparent/light QR modules can defeat adaptive binarization.
        // Retry with a high-contrast image; keep the same session-token validation.
        for (let i = 0; i < pixels.length; i += 4) {
          const value = pixels[i] * 77 + pixels[i + 1] * 150 + pixels[i + 2] * 29 >= 192 * 256 ? 255 : 0;
          pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
        }
      }
      // Templates may contain static QR codes as well as the guest's delivery QR.
      for (let attempt = 0; attempt < 8; attempt++) {
        const code = jsQR(pixels, data.width, data.height, { inversionAttempts: "attemptBoth" });
        if (!code) break;
        if (await matchesSession(code.data, data.sessionId)) { self.postMessage({ url: code.data }); return; }
        const corners = [code.location.topLeftCorner, code.location.topRightCorner, code.location.bottomLeftCorner, code.location.bottomRightCorner];
        const left = Math.max(0, Math.floor(Math.min(...corners.map(point => point.x))) - 4);
        const right = Math.min(data.width, Math.ceil(Math.max(...corners.map(point => point.x))) + 4);
        const top = Math.max(0, Math.floor(Math.min(...corners.map(point => point.y))) - 4);
        const bottom = Math.min(data.height, Math.ceil(Math.max(...corners.map(point => point.y))) + 4);
        for (let y = top; y < bottom; y++) pixels.fill(255, (y * data.width + left) * 4, (y * data.width + right) * 4);
      }
    }
    self.postMessage({ message: "No matching guest QR was found in this final image. Try again, or download the final and scan its QR." });
  } catch {
    self.postMessage({ message: "This image could not be scanned. Try again, or download the final and scan its QR." });
  }
};
