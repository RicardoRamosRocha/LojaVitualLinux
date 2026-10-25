import { proxyCartRequest } from "@/app/api/cart/proxy";

export function GET(request: Request) {
  const url = new URL(request.url);
  const shippingMethod = url.searchParams.get("shippingMethod") ?? "";
  return proxyCartRequest(
    request,
    `/api/orders/quote?shippingMethod=${encodeURIComponent(shippingMethod)}`,
  );
}
