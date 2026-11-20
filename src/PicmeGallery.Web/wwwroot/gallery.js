"use strict";
(() => {
  const byId = id => document.getElementById(id);
  const ownerSession = /^\/e\/([A-Za-z0-9_-]{43})\/s\/([0-9a-f]{64})\/?$/.exec(location.pathname);
  const token = ownerSession?.[1] || /^\/p\/([A-Za-z0-9_-]{43})\/?$/.exec(location.pathname)?.[1];
  const galleryUrl = ownerSession ? `/api/event/${token}/sessions/${ownerSession[2]}` : `/api/guest/${token}`;
  if (ownerSession) {
    const back = byId("event-back"); back.hidden = false; back.href = `/e/${token}`;
  }
  const status = byId("status"), refresh = byId("refresh");
  const waiting = byId("waiting"), grid = byId("originals"), dialog = byId("preview");
  const previewImage = byId("preview-image"), shareButton = byId("share-photo"), saveStatus = byId("save-status");
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const android = /Android/i.test(navigator.userAgent), mobile = ios || android;
  const assets = new Map();
  let timer, busy = false, polls = 0, selectedId, preparedFile, preparation, sharing = false, returnFocus;
  const orderedPhotos = () => [...assets.values()].sort((a, b) => a.id === "final" ? -1 : b.id === "final" ? 1 : a.id.localeCompare(b.id));
  byId("save-final").hidden = !mobile;
  byId("save-help").textContent = ios
    ? "Tap Save to Photos to prepare the original, then tap again and choose Save Image."
    : android
      ? "Tap Save / share to prepare the original, then tap again to save it to your gallery."
      : "Download the original in full quality.";

  function imageSource(image, url, originalUrl) {
    image.dataset.originalUrl = originalUrl || url; image.src = url;
  }
  function fallbackImage(image) {
    const original = image.dataset.originalUrl;
    if (!original || image.src === new URL(original, location.href).href) return false;
    image.src = original; return true;
  }
  function updateNavigation() {
    const photos = orderedPhotos(), index = photos.findIndex(photo => photo.id === selectedId);
    byId("preview-count").textContent = `${index + 1} / ${photos.length}`;
    byId("previous-photo").hidden = byId("next-photo").hidden = photos.length < 2;
  }
  async function prepareShare(photo) {
    shareButton.hidden = true;
    if (!window.isSecureContext || !navigator.share || !navigator.canShare || photo.bytes > 50 * 1024 * 1024) return;
    try {
      if (!navigator.canShare({ files: [new File([""], photo.fileName, { type: photo.contentType })] })) return;
    } catch { return; }
    const controller = new AbortController();
    preparation = controller;
    const timeout = setTimeout(() => controller.abort(), 30000);
    shareButton.hidden = false;
    shareButton.disabled = true;
    shareButton.textContent = "Preparing photo…";
    try {
      const response = await fetch(photo.fileUrl, { cache: "no-store", signal: controller.signal });
      if (!response.ok) throw new Error("File unavailable");
      const blob = await response.blob();
      if (!blob.size || blob.type.split(";")[0] !== photo.contentType) throw new Error("Unexpected image format");
      const file = new File([blob], photo.fileName, { type: photo.contentType });
      if (preparation !== controller || !dialog.open) return;
      if (!navigator.canShare({ files: [file] })) { shareButton.hidden = true; return; }
      preparedFile = file;
      shareButton.textContent = ios ? "Save to Photos" : android ? "Save / share" : "Share photo";
      shareButton.disabled = false;
    } catch {
      if (preparation !== controller || !dialog.open) return;
      shareButton.hidden = true;
      saveStatus.textContent = "The share option couldn’t be prepared. Download the original, or touch and hold the photo to save it.";
    } finally { clearTimeout(timeout); }
  }
  function openPreview(id, trigger) {
    const photo = assets.get(id);
    if (!photo || sharing) return;
    preparation?.abort(); preparation = null; preparedFile = null;
    selectedId = id;
    if (!dialog.open) { returnFocus = trigger || document.activeElement; dialog.showModal(); document.body.classList.add("preview-open"); }
    saveStatus.textContent = "";
    byId("preview-title").textContent = photo.label;
    previewImage.alt = photo.label;
    imageSource(previewImage, photo.previewUrl, photo.originalUrl);
    byId("download-photo").href = photo.downloadUrl;
    byId("download-photo").download = photo.fileName;
    updateNavigation();
    shareButton.hidden = true; shareButton.disabled = false;
    if (window.isSecureContext && navigator.share && navigator.canShare && photo.bytes <= 50 * 1024 * 1024) {
      try { shareButton.hidden = !navigator.canShare({ files: [new File([""], photo.fileName, { type: photo.contentType })] }); }
      catch { shareButton.hidden = true; }
    }
    shareButton.textContent = ios ? "Save to Photos" : android ? "Save / share" : "Share photo";
  }
  function movePreview(direction) {
    const photos = orderedPhotos(), index = photos.findIndex(photo => photo.id === selectedId);
    if (photos.length > 1) openPreview(photos[(index + direction + photos.length) % photos.length].id);
  }
  byId("close-preview").addEventListener("click", () => dialog.close());
  dialog.addEventListener("click", event => { if (event.target === dialog) {
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
  } });
  dialog.addEventListener("close", () => {
    preparation?.abort(); preparation = null; preparedFile = null;
    previewImage.removeAttribute("src"); document.body.classList.remove("preview-open");
    returnFocus?.focus();
  });
  dialog.addEventListener("keydown", event => {
    if (event.key === "ArrowLeft" || event.key === "ArrowRight") { event.preventDefault(); movePreview(event.key === "ArrowLeft" ? -1 : 1); }
  });
  byId("previous-photo").addEventListener("click", () => movePreview(-1));
  byId("next-photo").addEventListener("click", () => movePreview(1));
  let touchStart;
  previewImage.addEventListener("touchstart", event => {
    touchStart = event.touches.length === 1 ? { x: event.touches[0].clientX, y: event.touches[0].clientY, time: Date.now() } : null;
  }, { passive: true });
  previewImage.addEventListener("touchend", event => {
    if (!touchStart || !event.changedTouches.length) return;
    const dx = event.changedTouches[0].clientX - touchStart.x, dy = event.changedTouches[0].clientY - touchStart.y;
    if (Date.now() - touchStart.time < 600 && Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) movePreview(dx > 0 ? -1 : 1);
    touchStart = null;
  }, { passive: true });
  previewImage.addEventListener("error", () => { if (fallbackImage(previewImage)) return; saveStatus.textContent = "The preview couldn’t load. Try downloading the original, or close and reopen this preview."; });
  shareButton.addEventListener("click", async () => {
    if (sharing) return;
    if (!preparedFile) {
      const photo = assets.get(selectedId);
      if (photo) await prepareShare(photo);
      if (preparedFile && dialog.open) saveStatus.textContent = ios ? "Photo ready. Tap Save to Photos again." : "Photo ready. Tap Save / share again.";
      return;
    }
    saveStatus.textContent = "";
    sharing = true; shareButton.disabled = true;
    try {
      // The file is already loaded, so share() runs directly within the user's tap on iOS as well.
      await navigator.share({ files: [preparedFile] });
      if (dialog.open) saveStatus.textContent = "To keep another copy, use Download original.";
    } catch (error) {
      if (dialog.open && error.name !== "AbortError") saveStatus.textContent = "Sharing isn’t available here. Download the original, or touch and hold the photo to save it.";
    } finally { sharing = false; shareButton.disabled = false; }
  });
  byId("preview-final").addEventListener("click", event => openPreview("final", event.currentTarget));
  byId("save-final").addEventListener("click", event => openPreview("final", event.currentTarget));

  if (!token) {
    status.textContent = "Scan your print’s QR code.";
    byId("waiting-title").textContent = "Open your photos";
    byId("waiting-detail").textContent = "Use the QR code on your print.";
    return;
  }
  async function load(manual = false) {
    if (busy) return;
    let ready = false;
    clearTimeout(timer); busy = true; refresh.disabled = true;
    if (manual) {
      polls = 0;
      for (const image of grid.querySelectorAll('img[data-failed="true"]')) {
        image.dataset.failed = "false";
        image.src = image.dataset.previewUrl + (image.dataset.previewUrl.includes("?") ? "&" : "?") + "retry=" + Date.now();
      }
    }
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 25000);
    try {
      const response = await fetch(galleryUrl, { cache: "no-store", signal: controller.signal });
      if (response.status === 404) {
        if (dialog.open) dialog.close();
        assets.clear(); grid.replaceChildren(); byId("final-image").removeAttribute("src");
        byId("final-section").hidden = byId("originals-section").hidden = byId("event-info").hidden = true;
        waiting.hidden = false; byId("waiting-title").textContent = "This session is unavailable.";
        byId("waiting-detail").textContent = ownerSession ? "Return to the event gallery." : "Try Refresh later, or ask the event organizer.";
      }
      if (!response.ok) throw new Error(response.status === 404 ? (ownerSession ? "This event link or session is unavailable. Ask the organizer for a current link." : "This photo link could not be opened. Please scan the QR on your print again.") : "Your photos are temporarily unavailable. Please try again shortly.");
      const photos = await response.json(); status.classList.remove("error");
      byId("guest-event-id").textContent = photos.eventId || "";
      byId("event-info").hidden = !photos.eventId;
      if (photos.final) {
        assets.set("final", { ...photos.final, label: photos.final.contentType === "image/gif" ? "Your animated moment" : "Finished print" });
        const image = byId("final-image");
        if (!image.getAttribute("src")) imageSource(image, photos.final.previewUrl, photos.final.originalUrl);
        byId("final-title").textContent = assets.get("final").label;
        byId("download-final").href = photos.final.downloadUrl;
        byId("download-final").download = photos.final.fileName;
        byId("final-section").hidden = false; waiting.hidden = true;
      }
      for (const photo of photos.originals) {
        if (assets.has(photo.id)) continue;
        const number = Number(photo.id.slice(0, 3)), label = `Original photo ${number}`;
        assets.set(photo.id, { ...photo, label });
        const card = document.createElement("article"); card.className = "photo-card"; card.dataset.photoId = photo.id;
        const preview = document.createElement("button"); preview.type = "button"; preview.className = "photo-preview"; preview.setAttribute("aria-label", `Preview ${label.toLowerCase()}`);
        const image = document.createElement("img"); image.loading = "lazy"; image.alt = label; imageSource(image, photo.thumbnailUrl || photo.previewUrl, photo.originalUrl);
        image.dataset.previewUrl = photo.thumbnailUrl || photo.previewUrl;
        image.addEventListener("error", () => { if (fallbackImage(image)) return; image.dataset.failed = "true"; refresh.hidden = false; });
        const hint = document.createElement("span"); hint.className = "preview-hint"; hint.textContent = "Preview ↗"; hint.setAttribute("aria-hidden", "true");
        preview.append(image, hint); preview.addEventListener("click", () => openPreview(photo.id, preview));
        const actions = document.createElement("div"); actions.className = "card-actions";
        const photoLabel = document.createElement("span"); photoLabel.className = "photo-label"; photoLabel.textContent = `Photo ${number}`;
        const link = document.createElement("a"); link.className = "button secondary download-button"; link.href = photo.downloadUrl; link.download = photo.fileName; link.setAttribute("aria-label", `Download ${label.toLowerCase()}`);
        const icon = document.createElement("img"); icon.className = "download-icon"; icon.src = "/icons/download.svg"; icon.alt = ""; icon.width = 20; icon.height = 20; icon.setAttribute("aria-hidden", "true");
        link.append(icon, document.createTextNode("Download"));
        actions.append(photoLabel, link);
        if (mobile) {
          const save = document.createElement("button"); save.type = "button"; save.className = "save-small"; save.textContent = "Save"; save.setAttribute("aria-label", `Save ${label.toLowerCase()} to phone`);
          save.addEventListener("click", () => openPreview(photo.id, save)); actions.append(save);
        }
        card.append(preview, actions); grid.append(card);
      }
      for (const card of [...grid.children].sort((a, b) => a.dataset.photoId.localeCompare(b.dataset.photoId))) grid.append(card);
      const originals = grid.children.length;
      byId("originals-section").hidden = !originals;
      byId("photo-count").textContent = `${originals} / ${photos.expectedOriginals} photos`;
      if (dialog.open) updateNavigation();
      if (photos.state === "ready") status.textContent = "";
      else if (photos.final) status.textContent = `Uploading originals (${Math.max(originals, photos.uploadedOriginals)} of ${photos.expectedOriginals}).`;
      else status.textContent = "Uploading photos. This page checks automatically.";
      ready = photos.state === "ready";
      polls++;
      if (photos.state !== "ready" && polls < 40 && !document.hidden) timer = setTimeout(() => load(), polls < 5 ? 5000 : 15000);
    } catch (error) {
      status.textContent = error.name === "AbortError" ? "The booth photos are taking a little longer to open. Please try again." : error.message;
      status.classList.add("error");
    } finally {
      clearTimeout(timeout); busy = false; refresh.disabled = false;
      refresh.hidden = ready && !!byId("final-image").getAttribute("src") && !grid.querySelector('img[data-failed="true"]');
    }
  }
  refresh.addEventListener("click", () => load(true));
  document.addEventListener("visibilitychange", () => { if (document.hidden) clearTimeout(timer); else load(); });
  byId("final-image").addEventListener("error", event => {
    if (fallbackImage(event.target)) return;
    refresh.hidden = false;
    event.target.removeAttribute("src"); status.textContent = "The image could not load. Use Check for photos to try again.";
  });
  load();
})();
