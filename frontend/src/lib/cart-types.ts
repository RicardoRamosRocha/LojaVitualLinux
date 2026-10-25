export type CartItem = {
  productId: number;
  name: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
  imageUrl: string | null;
  availableStock: number;
};

export type CartResponse = {
  items: CartItem[];
  subtotal: number;
  totalQuantity: number;
};

export type CartError = {
  code?: string;
  message?: string;
  availableStock?: number;
};

export const emptyCart: CartResponse = {
  items: [],
  subtotal: 0,
  totalQuantity: 0,
};
