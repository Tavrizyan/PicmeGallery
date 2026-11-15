"use strict";
(() => {
  const byId = id => document.getElementById(id), status = byId("sessions-status"), filter = byId("session-filter");
  const eventId = /^\/admin\/events\/([a-z0-9][a-z0-9_-]{0,63})\/sessions\/?$/.exec(location.pathname)?.[1];
  const base = `/api/admin/events/${encodeURIComponent(eventId)}/sessions`, grid = byId("managed-sessions");
  const cards = new Map(), dialog = byId("session-preview");
  let csrf, version, nextCursor, loading = false, saving = false, returnFocus;
  function notice(message, error = false) { status.textContent = message; status.classList.toggle("error", error); }
  function controls() {
    filter.disabled = byId("refresh-sessions").disabled = byId("more-sessions").disabled = loading || saving;
    for (const button of grid.querySelectorAll(".visibility-toggle")) button.disabled = loading || saving;
  }
  async function request(url, body) {
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 30000);
    try {
      const response = await fetch(url, { cache: "no-store", signal: controller.signal, method: body === undefined ? "GET" : "POST",
        headers: body === undefined ? {} : { "Content-Type": "application/json", "X-CSRF-Token": csrf }, body: body === undefined ? undefined : JSON.stringify(body) });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (response.status === 401) location.assign("/admin/login");
        const error = new Error(data.message || (response.status === 404 ? "This event or session could not be found." : "The request could not be completed. Please try again."));
        error.status = response.status; throw error;
      }
      return data;
    } catch (error) { if (error.name === "AbortError") throw new Error("This request took too long. Please try Refresh sessions."); throw error; }
    finally { clearTimeout(timeout); }
  }
  function preview(session, button) {
    returnFocus = button; byId("session-preview-image").src = session.previewUrl;
    byId("session-preview-title").textContent = session.hidden ? "Hidden from event owner" : "Visible to event owner";
    byId("session-preview-status").textContent = "The guest’s printed QR continues to open this session.";
    dialog.showModal(); document.body.classList.add("preview-open");
  }
  byId("close-session-preview").addEventListener("click", () => dialog.close());
  dialog.addEventListener("close", () => { byId("session-preview-image").removeAttribute("src"); document.body.classList.remove("preview-open"); returnFocus?.focus(); });
  dialog.addEventListener("click", event => {
    if (event.target !== dialog) return;
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
  });
  byId("session-preview-image").addEventListener("error", () => { byId("session-preview-status").textContent = "This preview could not load. Close it and try Refresh sessions."; });
  function add(session) {
    if (cards.has(session.sessionId)) return;
    const number = cards.size + 1, card = document.createElement("article"); card.className = "photo-card";
    card.classList.toggle("hidden-session", session.hidden); card.dataset.sessionId = session.sessionId;
    const button = document.createElement("button"); button.className = "photo-preview"; button.type = "button";
    button.setAttribute("aria-label", `Preview session ${number}`);
    const image = document.createElement("img"); image.src = session.previewUrl; image.loading = "lazy"; image.alt = `Finished print for session ${number}`;
    const badge = document.createElement("span"); badge.className = "visibility-badge"; badge.textContent = session.hidden ? "Hidden" : "Visible";
    const hint = document.createElement("span"); hint.className = "preview-hint"; hint.textContent = "Preview ↗"; hint.setAttribute("aria-hidden", "true");
    button.append(image, badge, hint); button.addEventListener("click", () => preview(session, button));
    const actions = document.createElement("div"); actions.className = "card-actions managed-card-actions";
    const date = document.createElement("span"); date.className = "photo-label";
    const captured = new Date(session.capturedUtc);
    date.textContent = session.capturedUtc && !Number.isNaN(captured.valueOf()) ? captured.toLocaleString([], { dateStyle: "medium", timeStyle: "short" }) : `Session ${number}`;
    const toggle = document.createElement("button"); toggle.className = "secondary visibility-toggle"; toggle.type = "button";
    toggle.textContent = session.hidden ? "Restore to owner" : "Hide from owner";
    toggle.setAttribute("aria-label", `${session.hidden ? "Restore" : "Hide"} session ${number} ${session.hidden ? "for" : "from"} event owner`);
    toggle.addEventListener("click", () => changeVisibility(session));
    actions.append(date, toggle); card.append(button, actions); cards.set(session.sessionId, card); grid.append(card);
  }
  async function load(reset = false) {
    if (loading) return;
    loading = true; controls();
    try {
      const firstUrl = base + "?filter=" + encodeURIComponent(filter.value);
      let result = await request(firstUrl + (!reset && nextCursor ? "&cursor=" + encodeURIComponent(nextCursor) : ""));
      if (!reset && version && result.version !== version) { result = await request(firstUrl); reset = true; }
      if (reset) { if (dialog.open) dialog.close(); cards.clear(); grid.replaceChildren(); }
      version = result.version; nextCursor = result.nextCursor;
      byId("sessions-title").textContent = result.name; document.title = result.name + " · Manage sessions · PICME";
      byId("managed-event-id").textContent = "Event ID: " + result.eventId;
      byId("hidden-count").textContent = `${result.hiddenCount} ${result.hiddenCount === 1 ? "session" : "sessions"} hidden`;
      result.sessions.forEach(add); byId("no-sessions").hidden = !!cards.size; byId("more-sessions").hidden = !nextCursor;
    } finally { loading = false; controls(); }
  }
  async function changeVisibility(session) {
    if (loading || saving) return;
    saving = true; controls();
    try {
      await request(`${base}/${session.sessionId}/visibility`, { hidden: !session.hidden, version });
      await load(true);
      notice(session.hidden ? "Session restored to the event owner." : "Session hidden from the event owner. The guest QR still works.");
      const next = cards.get(session.sessionId)?.querySelector(".visibility-toggle");
      saving = false; controls(); (next || filter).focus();
    } catch (error) {
      if (error.status === 409) {
        try { await load(true); } catch { /* Preserve the conflict message; Refresh remains available. */ }
      }
      notice(error.message, true);
    } finally { saving = false; controls(); }
  }
  filter.addEventListener("change", () => load(true).then(() => notice("")).catch(error => notice(error.message, true)));
  byId("refresh-sessions").addEventListener("click", () => load(true).then(() => notice("Sessions updated.")).catch(error => notice(error.message, true)));
  byId("more-sessions").addEventListener("click", () => load().catch(error => notice(error.message, true)));
  (async () => {
    if (!eventId) { notice("Open an event’s Manage sessions page from administration.", true); return; }
    try {
      const session = await request("/api/admin/session");
      if (!session.authenticated) { location.replace("/admin/login"); return; }
      csrf = session.csrfToken; await load(true); notice("");
    } catch (error) { notice(error.message, true); }
  })();
})();
