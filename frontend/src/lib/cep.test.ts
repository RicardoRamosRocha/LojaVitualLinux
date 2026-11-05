import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { formatCep, normalizeCep } from "./cep.ts";

test("normalizes CEP to eight digits", () => {
  assert.equal(normalizeCep("01310-100abc"), "01310100");
  assert.equal(formatCep("01310100"), "01310-100");
  assert.equal(formatCep("0131"), "0131");
});

test("looks up and normalizes a ViaCEP response", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify({
    logradouro: "Avenida Paulista",
    bairro: "Bela Vista",
    localidade: "São Paulo",
    uf: "sp",
  }), { status: 200, headers: { "content-type": "application/json" } });

  try {
    // @ts-expect-error Node's native TypeScript runner requires the explicit extension.
    const { lookupCep } = await import("./cep.ts");
    assert.deepEqual(await lookupCep("01310-100"), {
      street: "Avenida Paulista",
      neighborhood: "Bela Vista",
      city: "São Paulo",
      state: "SP",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("rejects a CEP that ViaCEP cannot find", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify({ erro: true }), { status: 200 });

  try {
    // @ts-expect-error Node's native TypeScript runner requires the explicit extension.
    const { CepLookupError, lookupCep } = await import("./cep.ts");
    await assert.rejects(() => lookupCep("99999-999"), (error: unknown) =>
      error instanceof CepLookupError && error.kind === "not_found");
  } finally {
    globalThis.fetch = originalFetch;
  }
});
