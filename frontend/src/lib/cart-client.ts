import type { CartResponse, CartError } from "@/lib/cart-types";

type CartFetchResult = {
  cart: CartResponse;
  csrfToken: string | null;
};

async function parseResponse(response: Response): Promise<CartResponse> {
  const data = (await response.json().catch(() => null)) as CartResponse | CartError | null;
  if (!response.ok) {
    throw new Error((data as CartError | null)?.message ?? "Não foi possível atualizar o carrinho.");
  }
  return data as CartResponse;
}

export async function fetchCart(): Promise<CartFetchResult> {
  const response = await fetch("/api/cart", {
    cache: "no-store",
    credentials: "same-origin",
    headers: { Accept: "application/json" },
  });

  return {
    cart: await parseResponse(response),
    csrfToken: response.headers.get("X-CSRF-TOKEN"),
  };
}

async function mutateCart(
  url: string,
  method: "POST" | "PATCH" | "DELETE",
  csrfToken: string,
  body?: object,
) {
  const response = await fetch(url, {
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
  return mutateCart("/api/cart/items", "POST", csrfToken, { productId, quantity });
}

export function updateCartItem(productId: number, quantity: number, csrfToken: string) {
  return mutateCart(`/api/cart/items/${productId}`, "PATCH", csrfToken, { quantity });
}

export function removeCartItem(productId: number, csrfToken: string) {
  return mutateCart(`/api/cart/items/${productId}`, "DELETE", csrfToken);
}

export function clearCart(csrfToken: string) {
  return mutateCart("/api/cart", "DELETE", csrfToken);
}
