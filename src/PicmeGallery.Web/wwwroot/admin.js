"use strict";
(() => {
  const byId = id => document.getElementById(id), status = byId("admin-status");
  let csrf, nextCursor, loading = false, pendingAction;
  const notice = (message, error = false) => { status.textContent = message; status.classList.toggle("error", error); };
  async function request(url, body) {
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 30000);
    try {
      const response = await fetch(url, { method: body === undefined ? "GET" : "POST", cache: "no-store", signal: controller.signal,
        headers: body === undefined ? {} : { "Content-Type": "application/json", "X-CSRF-Token": csrf }, body: body === undefined ? undefined : JSON.stringify(body) });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (response.status === 401 && !url.endsWith("/login")) { location.assign("/admin/login"); throw new Error("Please sign in again."); }
        throw new Error(data.message || (response.status === 429 ? "Please wait a minute and try again." : "This request could not be completed. Please try again."));
      }
      return data;
    } catch (error) { if (error.name === "AbortError") throw new Error("This request took too long. Please try again."); throw error; }
    finally { clearTimeout(timeout); }
  }
  async function copy(input) {
    try { await navigator.clipboard.writeText(input.value); notice("Owner link copied."); }
    catch { input.focus(); input.select(); notice("Select and copy the owner link above."); }
  }
  function showLink(path) {
    const url = new URL(path, location.origin).href;
    byId("new-owner-url").value = url; byId("open-new-link").href = url; byId("created-link").hidden = false;
  }
  function eventCard(event) {
    const card = document.createElement("article"); card.className = "event-row";
    const heading = document.createElement("div"), name = document.createElement("h2"), detail = document.createElement("p");
    name.textContent = event.name;
    const created = new Date(event.createdUtc);
    const createdLabel = Number.isNaN(created.valueOf()) ? "" : ` · Created ${created.toLocaleString([], { dateStyle: "medium", timeStyle: "short" })}`;
    detail.textContent = `${event.eventId} · ${event.active ? "Active link" : "Link revoked"}${createdLabel}`;
    detail.className = "subtext"; heading.append(name, detail);
    const actions = document.createElement("div"); actions.className = "actions";
    const manage = document.createElement("a"); manage.className = "button secondary compact";
    manage.href = `/admin/events/${encodeURIComponent(event.eventId)}/sessions`; manage.textContent = "Manage sessions"; actions.append(manage);
    if (event.ownerPath) {
      const open = document.createElement("a"); open.className = "button secondary compact"; open.href = event.ownerPath; open.target = "_blank"; open.rel = "noopener noreferrer"; open.textContent = "Open gallery"; actions.append(open);
      const copyButton = document.createElement("button"); copyButton.className = "secondary compact"; copyButton.type = "button"; copyButton.textContent = "Copy link";
      copyButton.addEventListener("click", () => { showLink(event.ownerPath); copy(byId("new-owner-url")); }); actions.append(copyButton);
    }
    const replace = document.createElement("button"); replace.className = "secondary compact"; replace.type = "button"; replace.textContent = event.active ? "Replace link" : "Create new link";
    replace.addEventListener("click", () => confirm(event, "replace")); actions.append(replace);
    if (event.active) {
      const revoke = document.createElement("button"); revoke.type = "button"; revoke.className = "secondary compact danger"; revoke.textContent = "Revoke link";
      revoke.addEventListener("click", () => confirm(event, "revoke")); actions.append(revoke);
    }
    card.append(heading, actions); return card;
  }
  async function loadEvents(reset = false) {
    if (loading) return;
    loading = true; byId("refresh-events").disabled = byId("more-events").disabled = true;
    try {
      const result = await request("/api/admin/events" + (!reset && nextCursor ? "?cursor=" + encodeURIComponent(nextCursor) : ""));
      if (reset) byId("event-list").replaceChildren();
      for (const event of result.events) byId("event-list").append(eventCard(event));
      nextCursor = result.nextCursor; byId("more-events").hidden = !nextCursor;
      byId("empty-events").hidden = !!byId("event-list").children.length;
    } finally { loading = false; byId("refresh-events").disabled = byId("more-events").disabled = false; }
  }
  function confirm(event, action) {
    pendingAction = { event, action };
    byId("confirm-title").textContent = action === "revoke" ? "Revoke owner link?" : "Create a new owner link?";
    byId("confirm-detail").textContent = `For “${event.name}”, the previous owner link will stop working. Photos and printed guest QR links stay available.`;
    byId("accept-action").textContent = action === "revoke" ? "Revoke link" : "Create new link";
    byId("confirm-action").showModal();
  }
  byId("cancel-action").addEventListener("click", () => byId("confirm-action").close());
  byId("accept-action").addEventListener("click", async () => {
    const { event, action } = pendingAction; byId("accept-action").disabled = true;
    try {
      const result = await request(`/api/admin/events/${encodeURIComponent(event.eventId)}/${action}`, { version: event.version });
      byId("confirm-action").close(); byId("created-link").hidden = true;
      if (result.ownerPath) showLink(result.ownerPath);
      notice(action === "revoke" ? "Owner link revoked." : "New owner link created. The previous link no longer works."); await loadEvents(true);
    } catch (error) { byId("confirm-action").close(); notice(error.message, true); }
    finally { byId("accept-action").disabled = false; }
  });
  byId("login-form").addEventListener("submit", async event => {
    event.preventDefault(); byId("sign-in").disabled = true; notice("Signing in…");
    try { await request("/api/admin/login", { username: byId("username").value, password: byId("password").value }); byId("password").value = ""; location.replace("/admin"); }
    catch (error) { notice(error.message, true); byId("sign-in").disabled = false; }
  });
  byId("logout").addEventListener("click", async () => {
    try { await request("/api/admin/logout", {}); location.replace("/admin/login"); }
    catch (error) { notice(error.message, true); }
  });
  byId("create-event").addEventListener("submit", async event => {
    event.preventDefault(); byId("create-button").disabled = true;
    try {
      const result = await request("/api/admin/events", { eventId: byId("event-id").value, name: byId("event-name").value });
      showLink(result.ownerPath); byId("create-event").reset(); notice("Owner link created. Copy it for the organizer."); await loadEvents(true);
    } catch (error) { notice(error.message, true); }
    finally { byId("create-button").disabled = false; }
  });
  byId("copy-new-link").addEventListener("click", () => copy(byId("new-owner-url")));
  byId("refresh-events").addEventListener("click", () => loadEvents(true).then(() => notice("Event list updated.")).catch(error => notice(error.message, true)));
  byId("more-events").addEventListener("click", () => loadEvents().catch(error => notice(error.message, true)));
  (async () => {
    try {
      const session = await request("/api/admin/session"); csrf = session.csrfToken;
      if (!session.authenticated) {
        if (location.pathname !== "/admin/login") history.replaceState(null, "", "/admin/login");
        byId("login-panel").hidden = false; byId("username").focus(); notice(""); return;
      }
      if (location.pathname !== "/admin") history.replaceState(null, "", "/admin");
      byId("events-panel").hidden = false; byId("logout").hidden = false; byId("admin-user").textContent = session.username;
      await loadEvents(true); notice("");
    } catch (error) { notice(error.message, true); }
  })();
})();
