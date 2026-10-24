"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { fetchCart } from "@/lib/cart-client";
import { resolveMediaUrl } from "@/lib/api";
import type { CartResponse } from "@/lib/cart-types";
import { emptyCart } from "@/lib/cart-types";
import type { ShippingMethod } from "@/lib/order-types";

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const shippingCosts: Record<ShippingMethod, number> = { PAC: 15, SEDEX: 30, RETIRADA: 0 };

export function CheckoutView() {
  const [cart, setCart] = useState<CartResponse>(emptyCart);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [shippingMethod, setShippingMethod] = useState<ShippingMethod>("PAC");
  const [form, setForm] = useState({ customerName: "", email: "", phone: "", zipCode: "", address: "" });

  useEffect(() => {
    fetchCart()
      .then((result) => setCart(result.cart))
      .catch((caughtError) => setError(caughtError instanceof Error ? caughtError.message : "Não foi possível carregar o resumo."))
      .finally(() => setLoading(false));
  }, []);

  const shippingCost = useMemo(() => shippingCosts[shippingMethod], [shippingMethod]);
  const total = cart.subtotal + shippingCost;
  const requiresAddress = shippingMethod !== "RETIRADA";

  function updateField(field: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  return (
    <section className="checkout-shell">
      <div className="checkout-heading">
        <div><p className="eyebrow">Último cuidado</p><h1>Preparar pedido</h1></div>
        <Link className="back-link" href="/carrinho">← Voltar ao carrinho</Link>
      </div>

      {loading && <div className="cart-message">Carregando seu resumo...</div>}
      {error && <div className="cart-message cart-message--error" role="alert">{error}</div>}
      {!loading && !error && cart.items.length === 0 && (
        <div className="cart-message">
          <span className="catalog-message__eyebrow">Nada para finalizar</span>
          <h2>Seu carrinho está vazio.</h2>
          <p>Volte à coleção para escolher suas peças.</p>
          <Link className="hero-cta" href="/#colecao">Ver coleção <span>↘</span></Link>
        </div>
      )}
      {!loading && !error && cart.items.length > 0 && (
        <div className="checkout-layout">
          <form className="checkout-form" onSubmit={(event) => event.preventDefault()}>
            <div className="checkout-section-heading"><p className="eyebrow">Seus dados</p><h2>Onde entregamos?</h2></div>
            <div className="checkout-fields">
              <label>Nome completo<input required minLength={2} maxLength={120} value={form.customerName} onChange={(event) => updateField("customerName", event.target.value)} autoComplete="name" /></label>
              <label>E-mail<input required type="email" maxLength={180} value={form.email} onChange={(event) => updateField("email", event.target.value)} autoComplete="email" /></label>
              <label>Telefone<input required minLength={8} maxLength={30} value={form.phone} onChange={(event) => updateField("phone", event.target.value)} autoComplete="tel" /></label>
              <label>Método de entrega<select value={shippingMethod} onChange={(event) => setShippingMethod(event.target.value as ShippingMethod)}><option value="PAC">PAC — R$ 15,00</option><option value="SEDEX">SEDEX — R$ 30,00</option><option value="RETIRADA">Retirada — R$ 0,00</option></select></label>
              <label>CEP<input required={requiresAddress} maxLength={20} value={form.zipCode} onChange={(event) => updateField("zipCode", event.target.value)} autoComplete="postal-code" /></label>
              <label className="checkout-field--wide">Endereço<input required={requiresAddress} maxLength={240} value={form.address} onChange={(event) => updateField("address", event.target.value)} autoComplete="street-address" /></label>
            </div>
            <p className="checkout-hint">Os valores de frete são referências da regra atual do servidor. A validação definitiva será feita pelo backend.</p>
            <button className="cart-button" type="submit" disabled>Finalizar pedido</button>
            <p className="cart-note">Finalização e pagamento serão disponibilizados após a validação da API.</p>
          </form>

          <aside className="checkout-summary">
            <p className="eyebrow">Sua seleção</p><h2>Resumo</h2>
            <div className="checkout-summary__items">
              {cart.items.map((item) => {
                const image = resolveMediaUrl(item.imageUrl);
                return <div className="checkout-summary__item" key={item.productId}>
                  <div className="checkout-summary__image">{image ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={image} alt="" />
                  ) : <span>Angel</span>}</div>
                  <div><strong>{item.name}</strong><span>{item.quantity} × {currency.format(item.unitPrice)}</span></div>
                  <b>{currency.format(item.subtotal)}</b>
                </div>;
              })}
            </div>
            <div className="checkout-summary__line"><span>Subtotal</span><strong>{currency.format(cart.subtotal)}</strong></div>
            <div className="checkout-summary__line"><span>Frete ({shippingMethod})</span><strong>{currency.format(shippingCost)}</strong></div>
            <div className="checkout-summary__total"><span>Total</span><strong>{currency.format(total)}</strong></div>
          </aside>
        </div>
      )}
    </section>
  );
}
