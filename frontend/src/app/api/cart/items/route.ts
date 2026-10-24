import { proxyCartRequest } from "@/app/api/cart/proxy";

export function POST(request: Request) {
  return proxyCartRequest(request, "/api/cart/items");
}
