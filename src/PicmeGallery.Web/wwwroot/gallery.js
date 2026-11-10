"use strict";
(() => {
  const token = /^\/p\/([A-Za-z0-9_-]{43})\/?$/.exec(location.pathname)?.[1];
  const status = document.getElementById("status"), refresh = document.getElementById("refresh");
  const waiting = document.getElementById("waiting"), grid = document.getElementById("originals");
  let timer, busy = false, polls = 0;
  const visible = new Set();
  if (!token) {
    status.textContent = "Scan the QR on your photobooth print to open your photos.";
    document.getElementById("waiting-title").textContent = "Your print is your invitation.";
    document.getElementById("waiting-detail").textContent = "Each print has its own private photo link. No account needed.";
    return;
  }
  async function load(manual = false) {
    if (busy) return;
    clearTimeout(timer); busy = true; refresh.disabled = true;
    if (manual) {
      polls = 0;
      for (const image of grid.querySelectorAll('img[data-failed="true"]')) {
        image.dataset.failed = "false";
        image.src = image.dataset.previewUrl + "?retry=" + Date.now();
      }
    }
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 25000);
    try {
      const response = await fetch(`/api/guest/${token}`, { cache: "no-store", signal: controller.signal });
      if (!response.ok) throw new Error(response.status === 404 ? "This photo link could not be opened. Please scan the QR on your print again." : "Your photos are temporarily unavailable. Please try again shortly.");
      const photos = await response.json(); status.classList.remove("error");
      if (photos.final) {
        const image = document.getElementById("final-image");
        if (!image.getAttribute("src")) image.src = photos.final.previewUrl;
        document.getElementById("download-final").href = photos.final.downloadUrl;
        document.getElementById("final-section").hidden = false; waiting.hidden = true;
      }
      for (const photo of photos.originals) {
        if (visible.has(photo.id)) continue;
        visible.add(photo.id);
        const card = document.createElement("article"); card.className = "photo-card"; card.dataset.photoId = photo.id;
        const image = document.createElement("img"); image.loading = "lazy"; image.alt = `Original photo ${Number(photo.id.slice(0, 3))}`; image.src = photo.previewUrl;
        image.dataset.previewUrl = photo.previewUrl;
        image.addEventListener("error", () => { image.dataset.failed = "true"; });
        const link = document.createElement("a"); link.href = photo.downloadUrl; link.textContent = `Download photo ${Number(photo.id.slice(0, 3))} ↓`;
        card.append(image, link); grid.append(card);
      }
      for (const card of [...grid.children].sort((a, b) => a.dataset.photoId.localeCompare(b.dataset.photoId))) grid.append(card);
      document.getElementById("originals-section").hidden = !visible.size;
      document.getElementById("photo-count").textContent = `${visible.size} / ${photos.expectedOriginals} photos`;
      if (photos.state === "ready") status.textContent = "Your finished print and original photos are ready to download.";
      else if (photos.final) status.textContent = `Your print is ready. Original photos are still uploading (${Math.max(visible.size, photos.uploadedOriginals)} of ${photos.expectedOriginals}).`;
      else status.textContent = "Your photos may still be uploading. This page checks automatically.";
      polls++;
      if (photos.state !== "ready" && polls < 40 && !document.hidden) timer = setTimeout(() => load(), polls < 5 ? 5000 : 15000);
    } catch (error) {
      status.textContent = error.name === "AbortError" ? "The booth photos are taking a little longer to open. Please try again." : error.message;
      status.classList.add("error");
    } finally { clearTimeout(timeout); busy = false; refresh.hidden = false; refresh.disabled = false; }
  }
  refresh.addEventListener("click", () => load(true));
  document.addEventListener("visibilitychange", () => { if (document.hidden) clearTimeout(timer); else load(); });
  for (const image of [document.getElementById("final-image")]) image.addEventListener("error", () => {
    image.removeAttribute("src"); status.textContent = "The image could not load. Use Check for photos to try again.";
  });
  load();
})();
