import { proxyCartRequest } from "@/app/api/cart/proxy";

type RouteContext = {
  params: Promise<{ productId: string }>;
};

async function productPath(context: RouteContext) {
  const { productId } = await context.params;
  if (!/^\d+$/.test(productId)) return null;
  return `/api/cart/items/${productId}`;
}

export async function PATCH(request: Request, context: RouteContext) {
  const path = await productPath(context);
  if (!path) return Response.json({ message: "Produto inválido." }, { status: 400 });
  return proxyCartRequest(request, path);
}

export async function DELETE(request: Request, context: RouteContext) {
  const path = await productPath(context);
  if (!path) return Response.json({ message: "Produto inválido." }, { status: 400 });
  return proxyCartRequest(request, path);
}
