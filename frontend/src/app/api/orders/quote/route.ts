import { proxyCartRequest } from "@/app/api/cart/proxy";

export function GET(request: Request) {
  const url = new URL(request.url);
  const shippingMethod = url.searchParams.get("shippingMethod") ?? "";
  const zipCode = url.searchParams.get("zipCode") ?? "";
  return proxyCartRequest(
    request,
    `/api/orders/quote?shippingMethod=${encodeURIComponent(shippingMethod)}&zipCode=${encodeURIComponent(zipCode)}`,
  );
}
