import Link from "next/link";
import { Suspense } from "react";
import { OrderDetailView } from "@/components/order-detail-view";

type OrderDetailPageProps = { params: Promise<{ referencia: string }> };

async function OrderDetailContent({ params }: OrderDetailPageProps) {
  const { referencia } = await params;
  return <OrderDetailView reference={referencia} />;
}

export default function OrderDetailPage(props: OrderDetailPageProps) {
  return <main className="product-page"><div className="product-page__inner"><Link className="brand cart-brand" href="/"><span className="brand__mark">A</span><span><strong>Angel</strong><small>Acessórios</small></span></Link><Suspense fallback={<div className="cart-message" role="status">Carregando detalhes do pedido...</div>}><OrderDetailContent {...props} /></Suspense></div></main>;
}
