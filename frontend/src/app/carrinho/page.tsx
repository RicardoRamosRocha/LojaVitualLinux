import Link from "next/link";
import { CartView } from "@/components/cart-view";

export default function CartPage() {
  return (
    <main className="product-page">
      <div className="product-page__inner">
        <Link className="brand cart-brand" href="/">
          <span className="brand__mark">A</span>
          <span><strong>Angel</strong><small>Acessórios</small></span>
        </Link>
        <CartView />
      </div>
    </main>
  );
}
