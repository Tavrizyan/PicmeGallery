"use strict";
window.GuestLinks = (() => {
  const links = new Map();
  let dialog, input, status, copy, openLink, retry, current, operation, returnFocus;
  function initialize() {
    if (dialog) return;
    dialog = document.createElement("dialog"); dialog.className = "guest-link-dialog";
    dialog.setAttribute("aria-labelledby", "guest-link-title");
    dialog.innerHTML = '<div class="preview-toolbar"><h2 id="guest-link-title">Guest link</h2><button class="icon-button" type="button" aria-label="Close guest link" autofocus>×</button></div><div class="preview-bottom"><p>Share this session’s final and original photos with friends.</p><label for="guest-link-url">Printed QR link</label><input id="guest-link-url" readonly hidden><p id="guest-link-status" role="status" aria-live="polite"></p><div class="actions"><button id="copy-guest-url" type="button" disabled>Copy link</button><a id="open-guest-url" class="button secondary" target="_blank" rel="noopener noreferrer" hidden>Open link</a><button id="retry-guest-url" class="secondary" type="button" hidden>Retry</button></div></div>';
    document.body.append(dialog);
    input = dialog.querySelector("input"); status = dialog.querySelector("[role=status]");
    copy = dialog.querySelector("#copy-guest-url"); openLink = dialog.querySelector("#open-guest-url"); retry = dialog.querySelector("#retry-guest-url");
    dialog.querySelector(".icon-button").addEventListener("click", close);
    dialog.addEventListener("close", () => {
      operation?.controller.abort(); operation?.worker?.terminate(); operation = undefined; current = undefined;
      input.value = ""; openLink.removeAttribute("href");
      if (!document.querySelector("dialog[open]")) document.body.classList.remove("preview-open");
      returnFocus?.focus();
    });
    copy.addEventListener("click", async () => {
      if (!input.value) return;
      try { await navigator.clipboard.writeText(input.value); status.textContent = "Guest link copied."; }
      catch { input.focus(); input.select(); status.textContent = "Select and copy the link above."; }
    });
    retry.addEventListener("click", () => scan());
  }
  function showLink(url) {
    input.value = url; input.hidden = false; copy.disabled = false;
    openLink.href = url; openLink.hidden = false; status.textContent = "This is the guest link printed in the final image’s QR.";
  }
  async function scan() {
    const target = current, active = { controller: new AbortController() };
    operation = active;
    input.hidden = true; input.value = ""; copy.disabled = true; openLink.hidden = retry.hidden = true;
    openLink.removeAttribute("href"); status.textContent = "Reading the QR from the original final image…";
    const timeout = setTimeout(() => active.controller.abort(), 30000);
    let image;
    try {
      const url = new URL(target.photo.fileUrl, location.origin);
      if (url.origin !== location.origin) throw new Error("The original final image is unavailable. Refresh the gallery and try again.");
      if (target.photo.bytes > 50 * 1024 * 1024) throw new Error("This final is too large to scan here. Download it and scan its QR.");
      const response = await fetch(url, { cache: "no-store", signal: active.controller.signal });
      if (!response.ok) throw new Error("The original final image is unavailable. Refresh the gallery and try again.");
      const blob = await response.blob();
      if (!blob.size || blob.size > 50 * 1024 * 1024 || !["image/jpeg", "image/png", "image/gif"].includes(blob.type.split(";")[0]))
        throw new Error("This final could not be read. Download it and scan its QR.");
      active.controller.signal.throwIfAborted();
      image = await createImageBitmap(blob);
      active.controller.signal.throwIfAborted();
      const scale = Math.min(1, 4096 / Math.max(image.width, image.height), Math.sqrt(12000000 / (image.width * image.height)));
      const canvas = document.createElement("canvas");
      canvas.width = Math.max(1, Math.round(image.width * scale)); canvas.height = Math.max(1, Math.round(image.height * scale));
      const context = canvas.getContext("2d", { willReadFrequently: true });
      context.fillStyle = "white"; context.fillRect(0, 0, canvas.width, canvas.height);
      context.drawImage(image, 0, 0, canvas.width, canvas.height);
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
      canvas.width = canvas.height = 0;
      const result = await new Promise((resolve, reject) => {
        const worker = active.worker = new Worker("/guest-link-worker.js");
        active.controller.signal.addEventListener("abort", () => reject(new DOMException("Scan canceled", "AbortError")), { once: true });
        worker.onmessage = event => resolve(event.data);
        worker.onerror = () => reject(new Error("The QR scanner could not load. Try again."));
        worker.postMessage({ pixels: pixels.data.buffer, width: pixels.width, height: pixels.height, sessionId: target.sessionId }, [pixels.data.buffer]);
      });
      if (operation !== active || !dialog.open) return;
      if (!result.url) throw new Error(result.message);
      links.set(target.sessionId, result.url); showLink(result.url);
    } catch (error) {
      if (operation !== active || !dialog.open) return;
      status.textContent = active.controller.signal.aborted ? "Scanning took too long. Try again." : error.message;
      retry.hidden = false;
    } finally {
      clearTimeout(timeout); active.worker?.terminate();
      image?.close();
      if (operation === active) operation = undefined;
    }
  }
  function open(sessionId, photo, trigger) {
    initialize(); current = { sessionId, photo }; returnFocus = trigger;
    dialog.showModal(); document.body.classList.add("preview-open");
    retry.hidden = true;
    if (links.has(sessionId)) showLink(links.get(sessionId)); else scan();
  }
  function close() { if (dialog?.open) dialog.close(); }
  return { open, close };
})();
