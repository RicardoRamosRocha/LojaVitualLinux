import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { proxyCartRequest } from "./proxy.ts";

test("forwards session, CSRF, and idempotency headers and returns Set-Cookie", async () => {
  const originalFetch = globalThis.fetch;
  let forwardedRequest: Request | undefined;

  globalThis.fetch = async (input, init) => {
    forwardedRequest = new Request(input, init);
    return new Response(JSON.stringify({ items: [] }), {
      status: 200,
      headers: {
        "Content-Type": "application/json",
        "Set-Cookie": "session=test; Path=/; HttpOnly; SameSite=Lax",
        "X-CSRF-TOKEN": "response-token",
      },
    });
  };

  try {
    const request = new Request("https://frontend.example/api/cart", {
      method: "POST",
      headers: {
        Cookie: "session=client-session",
        RequestVerificationToken: "request-token",
        "Idempotency-Key": "checkout-key",
        "Content-Type": "application/json",
      },
      body: "{}",
    });
    const response = await proxyCartRequest(request, "/api/cart/items");

    assert.equal(forwardedRequest?.headers.get("Cookie"), "session=client-session");
    assert.equal(forwardedRequest?.headers.get("RequestVerificationToken"), "request-token");
    assert.equal(forwardedRequest?.headers.get("Idempotency-Key"), "checkout-key");
    assert.equal(response.headers.get("X-CSRF-TOKEN"), "response-token");
    assert.match(response.headers.get("set-cookie") ?? "", /session=test/);
    assert.equal(response.headers.get("cache-control"), "private, no-store");
  } finally {
    globalThis.fetch = originalFetch;
  }
});
