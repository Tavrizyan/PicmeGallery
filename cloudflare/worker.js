export default {
  async fetch(request, env) {
    const publicUrl = new URL(request.url);
    let origin;
    try { origin = new URL(env.AZURE_ORIGIN); } catch { return new Response("Gallery origin is not configured.", { status: 503 }); }
    if (origin.protocol !== "https:" || !origin.hostname.endsWith(".azurewebsites.net") || origin.pathname !== "/" ||
        origin.username || origin.password || origin.search || origin.hash || (origin.port && origin.port !== "443"))
      return new Response("Gallery origin is not configured.", { status: 503 });
    const target = new URL(origin);
    target.pathname = publicUrl.pathname;
    target.search = publicUrl.search;
    const headers = new Headers(request.headers);
    headers.delete("host");
    headers.delete("forwarded");
    headers.delete("x-forwarded-host");
    headers.delete("x-forwarded-proto");
    const upstream = await fetch(target, {
      method: request.method, headers, body: ["GET", "HEAD"].includes(request.method) ? undefined : request.body,
      redirect: "manual"
    });
    const response = new Response(upstream.body, upstream);
    response.headers.set("Cache-Control", "private, no-store");
    response.headers.set("Referrer-Policy", "no-referrer");
    const location = response.headers.get("Location");
    if (location) {
      const redirect = new URL(location, origin);
      if (redirect.origin === origin.origin) {
        redirect.protocol = publicUrl.protocol; redirect.host = publicUrl.host;
        response.headers.set("Location", redirect.toString());
      }
    }
    return response;
  }
};
