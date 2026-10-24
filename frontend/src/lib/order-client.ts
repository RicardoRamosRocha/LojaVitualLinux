import type { CreateOrderRequest, OrderError, OrderResponse } from "@/lib/order-types";

async function parseOrderResponse(response: Response): Promise<OrderResponse> {
  const data = (await response.json().catch(() => null)) as OrderResponse | OrderError | null;
  if (!response.ok) {
    throw new Error((data as OrderError | null)?.message ?? "Não foi possível preparar o pedido.");
  }
  return data as OrderResponse;
}
export async function prepareOrder(request: CreateOrderRequest, csrfToken: string) {
  const response = await fetch("/api/orders", {
    method: "POST",
    cache: "no-store",
    credentials: "same-origin",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      RequestVerificationToken: csrfToken,
    },
    body: JSON.stringify(request),
  });

  return parseOrderResponse(response);
}
