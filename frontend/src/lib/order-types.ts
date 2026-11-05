import type { CartResponse } from "@/lib/cart-types";

export type ShippingMethod = "PAC" | "SEDEX" | "RETIRADA";

export type CreateOrderRequest = {
  customerName: string;
  email: string;
  phone: string;
  zipCode?: string;
  address?: string;
  addressNumber?: string;
  addressComplement?: string;
  neighborhood?: string;
  city?: string;
  state?: string;
  shippingMethod: ShippingMethod;
};
export type OrderApiErrorItem = {
  productId: number;
  code: string;
  message: string;
  availableStock?: number;
};
export type OrderItemResponse = {
  productId: number;
  quantity: number;
  unitPrice: number;
  subtotal: number;
};

export type OrderResponse = {
  id: number;
  status: string;
  createdAt?: string;
  shippingMethod?: string;
  subtotal: number;
  shippingCost: number;
  total: number;
  paymentStatus?: string;
  checkoutUrl?: string;
  items: OrderItemResponse[];
};

export type OrderQuoteResponse = {
  shippingMethod: ShippingMethod;
  subtotal: number;
  shippingCost: number;
  total: number;
};

export type OrderError = {
  code?: string;
  message?: string;
  items?: OrderApiErrorItem[];
};

export type CheckoutCart = CartResponse;
