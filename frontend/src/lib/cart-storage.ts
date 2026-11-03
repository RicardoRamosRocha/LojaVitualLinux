import type { CartResponse } from "@/lib/cart-types";

const storageKey = "angel-cart-items-v1";

export type StoredCartItem = {
  productId: number;
  quantity: number;
};

function getStorage() {
  if (typeof window === "undefined") return null;
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function readStoredCart(): StoredCartItem[] {
  const storage = getStorage();
  if (!storage) return [];

  try {
    const parsed: unknown = JSON.parse(storage.getItem(storageKey) ?? "[]");
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is StoredCartItem =>
      Boolean(item)
      && typeof item === "object"
      && Number.isInteger((item as StoredCartItem).productId)
      && (item as StoredCartItem).productId > 0
      && Number.isInteger((item as StoredCartItem).quantity)
      && (item as StoredCartItem).quantity > 0
      && (item as StoredCartItem).quantity <= 1000,
    );
  } catch {
    return [];
  }
}

export function writeStoredCart(cart: CartResponse) {
  const storage = getStorage();
  if (!storage) return;

  try {
    const items = cart.items.map(({ productId, quantity }) => ({ productId, quantity }));
    if (items.length === 0) storage.removeItem(storageKey);
    else storage.setItem(storageKey, JSON.stringify(items));
  } catch {
    // localStorage pode estar indisponível por política de privacidade/quota.
  }
}

export function clearStoredCart() {
  getStorage()?.removeItem(storageKey);
}
