import Link from "next/link";
import { CheckoutView } from "@/components/checkout-view";

export default function CheckoutPage() {
  return (
    <main className="product-page">
      <div className="product-page__inner">
        <Link className="brand cart-brand" href="/">
          <span className="brand__mark">A</span>
          <span><strong>Angel</strong><small>Acessórios</small></span>
        </Link>
        <CheckoutView />
      </div>
    </main>
  );
}
