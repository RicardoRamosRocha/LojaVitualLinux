import type { CartResponse } from "@/lib/cart-types";

export type ShippingMethod = string;

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
  shippingQuoteId?: string;
  shippingServiceId?: string;
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
  shippingProvider?: string;
  shippingServiceId?: string;
  shippingServiceName?: string;
  shippingDeliveryTime?: number;
  total: number;
  paymentStatus?: string;
  checkoutUrl?: string;
  items: OrderItemResponse[];
};

export type OrderQuoteResponse = {
  shippingMethod: ShippingMethod;
  isRealQuote: boolean;
  canCreateOrder: boolean;
  quoteStatus: string;
  quoteId?: string;
  expiresAtUtc?: string;
  subtotal: number;
  shippingCost: number;
  total: number;
  options: ShippingOption[];
};

export type ShippingOption = {
  serviceId: string;
  serviceName: string;
  providerName: string;
  price: number;
  deliveryTimeDays?: number | null;
};

export type OrderError = {
  code?: string;
  message?: string;
  items?: OrderApiErrorItem[];
};

export type CheckoutCart = CartResponse;
