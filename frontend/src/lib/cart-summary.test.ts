import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { getCartQuantity } from "./cart-summary.ts";

test("counts all units, not only distinct products", () => {
  assert.equal(getCartQuantity({
    items: [
      { productId: 1, quantity: 3, name: "Colar", unitPrice: 10, subtotal: 30, imageUrl: null, availableStock: 5 },
      { productId: 2, quantity: 2, name: "Anel", unitPrice: 20, subtotal: 40, imageUrl: null, availableStock: 5 },
    ],
    subtotal: 70,
    totalQuantity: 5,
  }), 5);
});
