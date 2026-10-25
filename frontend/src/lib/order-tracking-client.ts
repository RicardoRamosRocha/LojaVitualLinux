import type { OrderResponse } from "@/lib/order-types";

export class OrderTrackingApiError extends Error {
  constructor(readonly status: number) {
    super("Não foi possível carregar seus pedidos.");
    this.name = "OrderTrackingApiError";
  }
}

function isOrderResponse(value: unknown): value is OrderResponse {
  if (!value || typeof value !== "object") return false;

  const order = value as Partial<OrderResponse>;
  return typeof order.id === "number" &&
    typeof order.status === "string" &&
    typeof order.subtotal === "number" &&
    typeof order.shippingCost === "number" &&
    typeof order.total === "number" &&
    Array.isArray(order.items);
}

export async function fetchAuthorizedOrders(): Promise<OrderResponse[]> {
  let response: Response;
  try {
    response = await fetch("/api/orders/authorized", {
      headers: { Accept: "application/json" },
      credentials: "same-origin",
      cache: "no-store",
    });
  } catch {
    throw new OrderTrackingApiError(0);
  }

  if (response.status === 401) return [];
  if (!response.ok) throw new OrderTrackingApiError(response.status);

  const payload: unknown = await response.json();
  if (!Array.isArray(payload)) throw new OrderTrackingApiError(502);
  return payload.filter(isOrderResponse);
}
