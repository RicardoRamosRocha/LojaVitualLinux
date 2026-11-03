import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { groupProductsByCategory } from "./product-groups.ts";
import type { Product } from "./types.ts";

const product = (id: number, category: Product["category"]): Product => ({
  id,
  name: `Produto ${id}`,
  description: null,
  price: 10,
  stock: 1,
  categoryId: category?.id ?? null,
  category,
  isFeatured: false,
  isNewCollection: false,
  isBestSeller: false,
  imageUrl: null,
  videoUrl: null,
  imageUrls: [],
  videoUrls: [],
});

test("agrupa produtos pela categoria real e preserva os sem categoria", () => {
  const groups = groupProductsByCategory([
    product(1, { id: 2, name: "Colares" }),
    product(2, { id: 2, name: "Colares" }),
    product(3, null),
  ]);

  assert.deepEqual(groups.map(({ key, name, products }) => [key, name, products.length]), [
    ["category-2", "Colares", 2],
    ["uncategorized", "Sem categoria", 1],
  ]);
});
