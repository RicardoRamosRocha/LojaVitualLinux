import { proxyCartRequest } from "@/app/api/cart/proxy";

export function GET(request: Request) {
  return proxyCartRequest(request, "/api/cart");
}

export function DELETE(request: Request) {
  return proxyCartRequest(request, "/api/cart");
}
