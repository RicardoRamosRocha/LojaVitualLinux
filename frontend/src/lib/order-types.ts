import type { CartResponse } from "@/lib/cart-types";

export type ShippingMethod = "PAC" | "SEDEX" | "RETIRADA";

export type CreateOrderRequest = {
  customerName: string;
  email: string;
  phone: string;
  zipCode?: string;
  address?: string;
  shippingMethod: ShippingMethod;
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
  subtotal: number;
  shippingCost: number;
  total: number;
  items: OrderItemResponse[];
};

export type OrderError = {
  code?: string;
  message?: string;
  items?: Array<{
    productId: number;
    code: string;
    message: string;
    availableStock?: number;
  }>;
};

export type CheckoutCart = CartResponse;
