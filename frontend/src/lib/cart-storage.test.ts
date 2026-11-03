import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { clearStoredCart, readStoredCart, writeStoredCart } from "./cart-storage.ts";

function installStorage() {
  const values = new Map<string, string>();
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: { localStorage: {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value),
      removeItem: (key: string) => values.delete(key),
    } },
  });
  return values;
}

test("stores only cart product ids and quantities", () => {
  installStorage();
  writeStoredCart({
    items: [{ productId: 4, quantity: 2, name: "Colar", unitPrice: 10, subtotal: 20, imageUrl: null, availableStock: 4 }],
    subtotal: 20,
    totalQuantity: 2,
  });
  assert.deepEqual(readStoredCart(), [{ productId: 4, quantity: 2 }]);
});

test("rejects malformed or unsafe stored quantities", () => {
  const values = installStorage();
  values.set("angel-cart-items-v1", JSON.stringify([
    { productId: 1, quantity: 1 },
    { productId: 2, quantity: 0 },
    { productId: 3, quantity: 1001 },
    { productId: "4", quantity: 1 },
  ]));
  assert.deepEqual(readStoredCart(), [{ productId: 1, quantity: 1 }]);
});

test("limpa o carrinho local somente quando solicitado", () => {
  const values = installStorage();
  values.set("angel-cart-items-v1", JSON.stringify([{ productId: 1, quantity: 1 }]));
  clearStoredCart();
  assert.deepEqual(readStoredCart(), []);
});
