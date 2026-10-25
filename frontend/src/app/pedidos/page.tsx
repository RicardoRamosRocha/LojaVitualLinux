import Link from "next/link";
import { OrderTrackingView } from "@/components/order-tracking-view";

export default function OrdersPage() {
  return <main className="product-page"><div className="product-page__inner"><Link className="brand cart-brand" href="/"><span className="brand__mark">A</span><span><strong>Angel</strong><small>Acessórios</small></span></Link><OrderTrackingView /></div></main>;
}
