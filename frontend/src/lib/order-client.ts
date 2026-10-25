import type { CreateOrderRequest, OrderError, OrderQuoteResponse, OrderResponse } from "@/lib/order-types";

const orderRequestTimeoutMs = 10000;

export class OrderApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly items: OrderError["items"];

  constructor(status: number, error: OrderError | null) {
    super(error?.message ?? "Não foi possível preparar o pedido.");
    this.name = "OrderApiError";
    this.status = status;
    this.code = error?.code;
    this.items = error?.items;
  }
}

export class OrderSubmissionUncertainError extends Error {
  constructor() {
    super("A resposta do pedido não chegou. O pedido pode ter sido criado.");
    this.name = "OrderSubmissionUncertainError";
  }
}

async function parseOrderResponse(response: Response): Promise<OrderResponse> {
  const data = (await response.json().catch(() => null)) as OrderResponse | OrderError | null;
  if (!response.ok) {
    throw new OrderApiError(response.status, data as OrderError | null);
  }
  return data as OrderResponse;
}

export async function getOrderQuote(shippingMethod: CreateOrderRequest["shippingMethod"]): Promise<OrderQuoteResponse> {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), orderRequestTimeoutMs);

  try {
    const response = await fetch(`/api/orders/quote?shippingMethod=${encodeURIComponent(shippingMethod)}`, {
      cache: "no-store",
      credentials: "same-origin",
      headers: { Accept: "application/json" },
      signal: controller.signal,
    });
    const data = (await response.json().catch(() => null)) as OrderQuoteResponse | OrderError | null;
    if (!response.ok) throw new OrderApiError(response.status, data as OrderError | null);
    return data as OrderQuoteResponse;
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new Error("A cotação do frete demorou para responder.");
    }
    throw error;
  } finally {
    window.clearTimeout(timeout);
  }
}

export async function prepareOrder(request: CreateOrderRequest, csrfToken: string, idempotencyKey: string) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), orderRequestTimeoutMs);

  try {
    const response = await fetch("/api/orders", {
      method: "POST",
      cache: "no-store",
      credentials: "same-origin",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
        RequestVerificationToken: csrfToken,
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify(request),
      signal: controller.signal,
    });

    return await parseOrderResponse(response);
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new OrderSubmissionUncertainError();
    }
    throw error;
  } finally {
    window.clearTimeout(timeout);
  }
}
