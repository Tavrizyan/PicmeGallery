"use strict";
(() => {
  const byId = id => document.getElementById(id), status = byId("event-status");
  const token = /^\/e\/([A-Za-z0-9_-]{43})\/?$/.exec(location.pathname)?.[1];
  const grid = byId("event-photos"), dialog = byId("event-preview"), sessions = new Map();
  let nextCursor, busy = false, timer, returnFocus, unavailable = false;
  function notice(message, error = false) { status.textContent = message; status.classList.toggle("error", error); }
  function preview(session, trigger) {
    returnFocus = trigger; byId("event-preview-image").src = session.final.previewUrl;
    byId("event-preview-title").textContent = session.final.contentType === "image/gif" ? "Animated moment" : "Finished print";
    byId("preview-final-download").href = session.final.downloadUrl; byId("preview-final-download").download = session.final.fileName;
    byId("preview-session").href = session.sessionPath; byId("event-preview-status").textContent = "";
    dialog.showModal(); document.body.classList.add("preview-open");
  }
  byId("close-event-preview").addEventListener("click", () => dialog.close());
  dialog.addEventListener("close", () => { byId("event-preview-image").removeAttribute("src"); document.body.classList.remove("preview-open"); returnFocus?.focus(); });
  dialog.addEventListener("click", event => {
    if (event.target !== dialog) return;
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
  });
  byId("event-preview-image").addEventListener("error", () => { byId("event-preview-status").textContent = "The preview could not load. Try downloading the final or opening all session photos."; });
  function add(session) {
    if (sessions.has(session.sessionId)) return;
    sessions.set(session.sessionId, session);
    const card = document.createElement("article"); card.className = "photo-card";
    const button = document.createElement("button"); button.className = "photo-preview"; button.type = "button";
    const number = sessions.size; button.setAttribute("aria-label", `Preview moment ${number}`);
    const image = document.createElement("img"); image.src = session.final.previewUrl; image.loading = "lazy"; image.alt = `Finished print ${number}`;
    const hint = document.createElement("span"); hint.className = "preview-hint"; hint.textContent = "Preview ↗"; hint.setAttribute("aria-hidden", "true");
    button.append(image, hint); button.addEventListener("click", () => preview(session, button));
    const actions = document.createElement("div"); actions.className = "card-actions owner-card-actions";
    const caption = document.createElement("span"); caption.className = "photo-label";
    const captured = new Date(session.final.capturedUtc);
    caption.textContent = session.final.capturedUtc && !Number.isNaN(captured.valueOf()) ? captured.toLocaleString([], { dateStyle: "medium", timeStyle: "short" }) : `Moment ${number}`;
    const download = document.createElement("a"); download.className = "button secondary download-button"; download.href = session.final.downloadUrl; download.download = session.final.fileName; download.setAttribute("aria-label", `Download final for moment ${number}`);
    const icon = document.createElement("img"); icon.className = "download-icon"; icon.src = "/icons/download.svg"; icon.alt = ""; icon.width = 16; icon.height = 16;
    download.append(icon, document.createTextNode("Final"));
    const all = document.createElement("a"); all.className = "button session-button"; all.href = session.sessionPath; all.textContent = "All photos"; all.setAttribute("aria-label", `All photos for moment ${number}`);
    actions.append(caption, download, all); card.append(button, actions); grid.append(card);
  }
  async function load({ more = false, scan = false } = {}) {
    if (busy || unavailable || !token) return;
    clearTimeout(timer); busy = true; byId("refresh-event").disabled = byId("more-photos").disabled = true;
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 120000);
    try {
      let cursor = more ? nextCursor : null, result;
      do {
        const response = await fetch(`/api/event/${token}` + (cursor ? "?cursor=" + encodeURIComponent(cursor) : ""), { cache: "no-store", signal: controller.signal });
        if (response.status === 404) {
          unavailable = true; sessions.clear(); grid.replaceChildren(); byId("download-all").hidden = true;
          byId("more-photos").hidden = true; byId("download-note").hidden = true; byId("event-empty").hidden = true;
          if (dialog.open) dialog.close(); byId("event-title").textContent = "Link unavailable"; document.title = "Link unavailable · PICME";
          byId("session-count").textContent = ""; byId("refresh-event").hidden = true;
          throw new Error("This event link is unavailable. Ask the organizer for a current link.");
        }
        if (!response.ok) throw new Error("The event gallery is temporarily unavailable. Please try again.");
        result = await response.json(); byId("event-title").textContent = result.name; document.title = result.name + " · PICME";
        result.sessions.forEach(add); cursor = result.nextCursor;
        if (!scan || document.hidden) break;
      } while (cursor);
      nextCursor = cursor; byId("more-photos").hidden = !nextCursor;
      byId("download-all").href = result.downloadAllUrl; byId("download-all").hidden = !sessions.size;
      byId("download-note").hidden = !sessions.size; byId("event-empty").hidden = !!sessions.size;
      byId("session-count").textContent = `${sessions.size} ${sessions.size === 1 ? "moment" : "moments"} loaded`;
      notice(sessions.size ? "Final prints only. Open All photos for originals." : "Waiting for the booth’s first uploads. This page checks automatically.");
    } catch (error) { notice(error.name === "AbortError" ? "The gallery is taking a while to open. Please try Refresh gallery." : error.message, true); }
    finally {
      clearTimeout(timeout); busy = false; byId("refresh-event").disabled = byId("more-photos").disabled = false;
      if (!document.hidden && !unavailable) timer = setTimeout(() => load({ scan: true }), 60000);
    }
  }
  byId("more-photos").addEventListener("click", () => load({ more: true }));
  byId("refresh-event").addEventListener("click", () => load({ scan: true }));
  byId("download-all").addEventListener("click", () => notice("Your ZIP download is starting. It includes all available final and original photos. Keep this page open while it downloads."));
  document.addEventListener("visibilitychange", () => { if (document.hidden) clearTimeout(timer); else load({ scan: true }); });
  load();
})();
