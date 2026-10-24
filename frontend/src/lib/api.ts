import type { Product, ProductResult, ProductsResult } from "@/lib/types";

const apiUrl = (process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5268").replace(
  /\/$/,
  "",
);

function isProduct(value: unknown): value is Product {
  if (!value || typeof value !== "object") return false;

  const product = value as Partial<Product>;
  return (
    typeof product.id === "number" &&
    typeof product.name === "string" &&
    typeof product.price === "number" &&
    typeof product.stock === "number"
  );
}

export function resolveMediaUrl(path: string | null | undefined) {
  if (!path) return null;
  if (/^https?:\/\//i.test(path)) return path;
  return `${apiUrl}/${path.replace(/^\/+/, "")}`;
}

export async function getProducts(): Promise<ProductsResult> {
  try {
    const response = await fetch(`${apiUrl}/api/products`, {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });

    if (!response.ok) {
      return { products: [], unavailable: true };
    }

    const data: unknown = await response.json();
    if (!Array.isArray(data)) {
      return { products: [], unavailable: true };
    }

    return {
      products: data.filter(isProduct),
      unavailable: false,
    };
  } catch {
    return { products: [], unavailable: true };
  }
}

export async function getProduct(id: string | number): Promise<ProductResult> {
  try {
    const response = await fetch(`${apiUrl}/api/products/${encodeURIComponent(id)}`, {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });

    if (response.status === 404) {
      return { product: null, unavailable: false, notFound: true };
    }

    if (!response.ok) {
      return { product: null, unavailable: true, notFound: false };
    }

    const data: unknown = await response.json();
    if (!isProduct(data)) {
      return { product: null, unavailable: true, notFound: false };
    }

    return { product: data, unavailable: false, notFound: false };
  } catch {
    return { product: null, unavailable: true, notFound: false };
  }
}
