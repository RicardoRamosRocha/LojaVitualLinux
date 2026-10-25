const backendUrl = (process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5268").replace(/\/$/, "");

export async function proxyCartRequest(request: Request, path: string) {
  const headers = new Headers({ Accept: "application/json" });
  const cookie = request.headers.get("cookie");
  const csrfToken = request.headers.get("RequestVerificationToken");
  const idempotencyKey = request.headers.get("Idempotency-Key");

  if (cookie) headers.set("Cookie", cookie);
  if (csrfToken) headers.set("RequestVerificationToken", csrfToken);
  if (idempotencyKey) headers.set("Idempotency-Key", idempotencyKey);
  if (request.method !== "GET" && request.method !== "DELETE") {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(`${backendUrl}${path}`, {
    method: request.method,
    headers,
    body: request.method === "GET" || request.method === "DELETE" ? undefined : await request.text(),
    cache: "no-store",
  });

  const responseHeaders = new Headers();
  responseHeaders.set("cache-control", "private, no-store");
  const contentType = response.headers.get("content-type");
  if (contentType) responseHeaders.set("content-type", contentType);

  const csrfResponseToken = response.headers.get("X-CSRF-TOKEN");
  if (csrfResponseToken) responseHeaders.set("X-CSRF-TOKEN", csrfResponseToken);

  const setCookies = response.headers.getSetCookie?.() ?? [];
  for (const setCookie of setCookies) {
    responseHeaders.append("set-cookie", setCookie);
  }

  if (setCookies.length === 0) {
    const setCookie = response.headers.get("set-cookie");
    if (setCookie) responseHeaders.set("set-cookie", setCookie);
  }

  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: responseHeaders,
  });
}
