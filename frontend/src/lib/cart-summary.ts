import type { CartResponse } from "@/lib/cart-types";

export function getCartQuantity(cart: CartResponse) {
  return cart.items.reduce((total, item) => total + item.quantity, 0);
}
