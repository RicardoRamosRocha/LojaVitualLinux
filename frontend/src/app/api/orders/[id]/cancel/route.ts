import { proxyCartRequest } from "@/app/api/cart/proxy";

export function POST(request: Request, context: { params: Promise<{ id: string }> }) {
  return context.params.then(({ id }) =>
    proxyCartRequest(request, `/api/orders/${encodeURIComponent(id)}/cancel`),
  );
}
