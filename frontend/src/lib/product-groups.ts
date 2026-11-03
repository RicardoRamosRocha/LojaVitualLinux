import type { Product } from "@/lib/types";

export type ProductGroup = {
  key: string;
  name: string;
  products: Product[];
};

export function groupProductsByCategory(products: Product[]): ProductGroup[] {
  const groups = new Map<string, ProductGroup>();

  for (const product of products) {
    const key = product.category ? `category-${product.category.id}` : "uncategorized";
    const current = groups.get(key);

    if (current) {
      current.products.push(product);
      continue;
    }

    groups.set(key, {
      key,
      name: product.category?.name ?? "Sem categoria",
      products: [product],
    });
  }

  return Array.from(groups.values());
}
