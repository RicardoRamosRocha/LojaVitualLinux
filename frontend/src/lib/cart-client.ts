import type { CartResponse, CartError } from "@/lib/cart-types";
import { readStoredCart, writeStoredCart } from "@/lib/cart-storage";

const cartRequestTimeoutMs = 10000;
const cartUpdatedEvent = "angel:cart-updated";

function publishCart(cart: CartResponse) {
  writeStoredCart(cart);
  if (typeof window !== "undefined") window.dispatchEvent(new Event(cartUpdatedEvent));
}

type CartFetchResult = {
  cart: CartResponse;
  csrfToken: string | null;
};

async function parseResponse(response: Response): Promise<CartResponse> {
  const data = (await response.json().catch(() => null)) as CartResponse | CartError | null;
  if (!response.ok) {
    const errorData = data as CartError | null;
    if (response.status === 409 && errorData?.code === "insufficient_stock" && typeof errorData.availableStock === "number") {
      const availableStock = errorData.availableStock;
      throw new Error(`Há apenas ${availableStock} unidade${availableStock === 1 ? "" : "s"} disponível${availableStock === 1 ? "" : "is"} deste produto.`);
    }
    throw new Error(errorData?.message ?? "Não foi possível atualizar o carrinho.");
  }
  return data as CartResponse;
}

async function fetchWithTimeout(input: RequestInfo | URL, init?: RequestInit) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), cartRequestTimeoutMs);

  try {
    return await fetch(input, { ...init, signal: controller.signal });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new Error("O servidor demorou para responder. Verifique a conexão e tente novamente.");
    }
    throw error;
  } finally {
    window.clearTimeout(timeout);
  }
}

export async function fetchCart(): Promise<CartFetchResult> {
  const response = await fetchWithTimeout("/api/cart", {
    cache: "no-store",
    credentials: "same-origin",
    headers: { Accept: "application/json" },
  });

  let cart = await parseResponse(response);
  const csrfToken = response.headers.get("X-CSRF-TOKEN");

  // The backend session is authoritative. This fallback only restores the
  // non-sensitive cart item ids/quantities after an in-memory session restart.
  if (cart.items.length === 0 && csrfToken) {
    for (const item of readStoredCart()) {
      try {
        cart = await addCartItem(item.productId, item.quantity, csrfToken);
      } catch {
        // Ignore stale/unavailable items and continue restoring the cart.
      }
    }
  }

  writeStoredCart(cart);
  return { cart, csrfToken };
}

async function mutateCart(
  url: string,
  method: "POST" | "PATCH" | "DELETE",
  csrfToken: string,
  body?: object,
) {
  const response = await fetchWithTimeout(url, {
    method,
    cache: "no-store",
    credentials: "same-origin",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      RequestVerificationToken: csrfToken,
    },
    body: body ? JSON.stringify(body) : undefined,
  });

  return parseResponse(response);
}

export function addCartItem(productId: number, quantity: number, csrfToken: string) {
  return mutateCart("/api/cart/items", "POST", csrfToken, { productId, quantity }).then((cart) => {
    publishCart(cart);
    return cart;
  });
}

export function updateCartItem(productId: number, quantity: number, csrfToken: string) {
  return mutateCart(`/api/cart/items/${productId}`, "PATCH", csrfToken, { quantity }).then((cart) => {
    publishCart(cart);
    return cart;
  });
}

export function removeCartItem(productId: number, csrfToken: string) {
  return mutateCart(`/api/cart/items/${productId}`, "DELETE", csrfToken).then((cart) => {
    publishCart(cart);
    return cart;
  });
}

export function clearCart(csrfToken: string) {
  return mutateCart("/api/cart", "DELETE", csrfToken).then((cart) => {
    publishCart(cart);
    return cart;
  });
}
