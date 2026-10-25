"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { fetchCart } from "@/lib/cart-client";
import { resolveMediaUrl } from "@/lib/api";
import type { CartResponse } from "@/lib/cart-types";
import { emptyCart } from "@/lib/cart-types";
import { getOrderQuote, OrderApiError, OrderSubmissionUncertainError, prepareOrder } from "@/lib/order-client";
import type { CreateOrderRequest, OrderQuoteResponse, OrderResponse, ShippingMethod } from "@/lib/order-types";

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const checkoutStorageKey = "angel-checkout-order-v1";

type CheckoutForm = {
  customerName: string;
  email: string;
  phone: string;
  zipCode: string;
  address: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
  shippingMethod: ShippingMethod;
};

const initialForm: CheckoutForm = {
  customerName: "", email: "", phone: "", zipCode: "", address: "", number: "",
  complement: "", neighborhood: "", city: "", state: "", shippingMethod: "PAC",
};

function createIdempotencyKey() {
  if (typeof crypto.randomUUID === "function") {
    return `web-checkout-${crypto.randomUUID()}`;
  }

  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0"));
  return `web-checkout-${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
}

function formatOrderError(error: unknown) {
  if (error instanceof OrderSubmissionUncertainError) {
    return "A resposta não chegou. O pedido pode ter sido criado; verifique Meus pedidos ou tente novamente usando o mesmo envio.";
  }
  if (!(error instanceof OrderApiError)) return "Não foi possível concluir agora. Confira sua conexão e tente novamente.";
  if (error.status === 400) {
    if (error.code === "empty_cart") return "Seu carrinho está vazio.";
    if (error.code === "missing_delivery_address") return "Preencha o endereço e o CEP para continuar.";
    if (error.code === "invalid_shipping_method") return "Escolha uma modalidade de entrega válida.";
    return error.message;
  }
  if (error.status === 401 || error.status === 403) return "Sua sessão expirou. Recarregue a página e tente novamente.";
  if (error.status === 409) {
    if (error.code === "idempotency_conflict") return "Este envio já foi associado a outro conteúdo. Recarregue o checkout.";
    if (error.code === "invalid_cart") return "Algum produto ficou sem estoque. Revise o carrinho.";
    return "O estoque mudou enquanto preparávamos o pedido. Revise o carrinho e tente novamente.";
  }
  if (error.status >= 500) return "O servidor não conseguiu preparar o pedido. Tente novamente em instantes.";
  return error.message;
}

function readStoredOrder(): { key: string; order?: OrderResponse } | null {
  try {
    const value = sessionStorage.getItem(checkoutStorageKey);
    return value ? JSON.parse(value) as { key: string; order?: OrderResponse } : null;
  } catch {
    return null;
  }
}

export function CheckoutView() {
  const [cart, setCart] = useState<CartResponse>(emptyCart);
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [cartError, setCartError] = useState<string | null>(null);
  const [orderError, setOrderError] = useState<string | null>(null);
  const [quote, setQuote] = useState<OrderQuoteResponse | null>(null);
  const [quoteError, setQuoteError] = useState<string | null>(null);
  const [order, setOrder] = useState<OrderResponse | null>(() => readStoredOrder()?.order ?? null);
  const [form, setForm] = useState<CheckoutForm>(initialForm);
  const [idempotencyKey, setIdempotencyKey] = useState(() => readStoredOrder()?.key ?? "");
  const [cartLoadAttempt, setCartLoadAttempt] = useState(0);

  useEffect(() => {
    fetchCart()
      .then((result) => {
        setCart(result.cart);
        setCsrfToken(result.csrfToken);
        setCartError(null);
      })
      .catch((caughtError) => setCartError(caughtError instanceof Error ? caughtError.message : "Não foi possível carregar o resumo."))
      .finally(() => setLoading(false));
  }, [cartLoadAttempt]);

  useEffect(() => {
    if (loading || cartError || cart.items.length === 0) return;

    getOrderQuote(form.shippingMethod)
      .then((result) => {
        setQuote(result);
        setQuoteError(null);
      })
      .catch((caughtError) => {
        setQuote(null);
        setQuoteError(caughtError instanceof Error ? caughtError.message : "Não foi possível calcular o frete.");
      });
  }, [cart.items.length, cart.subtotal, cartError, form.shippingMethod, loading]);

  const requiresAddress = form.shippingMethod !== "RETIRADA";

  function updateField(field: keyof CheckoutForm, value: string) {
    if (field === "shippingMethod") {
      setQuote(null);
      setQuoteError(null);
    }
    setForm((current) => ({ ...current, [field]: value }));
  }

  function retryCartLoad() {
    setCartError(null);
    setLoading(true);
    setCartLoadAttempt((attempt) => attempt + 1);
  }

  async function submitOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || order) return;
    if (!csrfToken) { setOrderError("Não foi possível validar sua sessão. Recarregue a página e tente novamente."); return; }
    if (!quote || quote.shippingMethod !== form.shippingMethod) {
      setOrderError("Aguarde o cálculo do frete antes de finalizar o pedido.");
      return;
    }

    setSubmitting(true);
    setOrderError(null);

    try {
      const key = idempotencyKey || createIdempotencyKey();
      setIdempotencyKey(key);
      sessionStorage.setItem(checkoutStorageKey, JSON.stringify({ key }));

      const addressParts = requiresAddress ? [
        `${form.address.trim()}, ${form.number.trim()}`,
        form.complement.trim(), form.neighborhood.trim(),
        `${form.city.trim()} - ${form.state.trim().toUpperCase()}`,
      ].filter(Boolean) : [];
      const request: CreateOrderRequest = {
        customerName: form.customerName.trim(), email: form.email.trim(), phone: form.phone.trim(),
        zipCode: form.zipCode.trim(), address: addressParts.join(" | "), shippingMethod: form.shippingMethod,
      };

      const created = await prepareOrder(request, csrfToken, key);
      sessionStorage.setItem(checkoutStorageKey, JSON.stringify({ key, order: created }));
      setOrder(created);
      setCart(emptyCart);
    } catch (caughtError) {
      setOrderError(formatOrderError(caughtError));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="checkout-shell">
      <div className="checkout-heading">
        <div><p className="eyebrow">Último cuidado</p><h1>{order ? "Pedido recebido" : "Preparar pedido"}</h1></div>
        {!order && <Link className="back-link" href="/carrinho">← Voltar ao carrinho</Link>}
      </div>
      {loading && <div className="cart-message">Carregando seu resumo...</div>}
      {cartError && (
        <div className="cart-message cart-message--error" role="alert">
          <p>Não foi possível carregar o resumo do carrinho. {cartError}</p>
          <button className="cart-button cart-button--retry" type="button" onClick={retryCartLoad}>Tentar novamente</button>
        </div>
      )}
      {orderError && (
        <div className="cart-message cart-message--error" role="alert">
          <p>{orderError}</p>
          {orderError.startsWith("A resposta não chegou") && <Link className="back-link" href="/pedidos">Ver meus pedidos</Link>}
        </div>
      )}
      {!loading && order && (
        <div className="checkout-confirmation" role="status">
          <p className="eyebrow">Pedido #{order.id}</p>
          <h2>Seu pedido está pendente.</h2>
          <p>Recebemos os itens e reservamos o estoque. Este pedido ainda não representa pagamento confirmado.</p>
          <div className="checkout-confirmation__total"><span>Total do pedido</span><strong>{currency.format(order.total)}</strong></div>
          <Link className="cart-button cart-button--link" href={`/pedidos/${order.id}`}>Acompanhar pedido</Link>
          <Link className="hero-cta" href="/">Voltar à coleção <span>↗</span></Link>
        </div>
      )}
      {!loading && !order && !cartError && cart.items.length === 0 && (
        <div className="cart-message"><span className="catalog-message__eyebrow">Nada para finalizar</span><h2>Seu carrinho está vazio.</h2><p>Volte à coleção para escolher suas peças.</p><Link className="hero-cta" href="/#colecao">Ver coleção <span>↗</span></Link></div>
      )}
      {!loading && !order && cart.items.length > 0 && (
        <div className="checkout-layout">
          <form className="checkout-form" onSubmit={submitOrder}>
            <div className="checkout-section-heading"><p className="eyebrow">Seus dados</p><h2>Onde entregamos?</h2></div>
            <div className="checkout-fields">
              <label>Nome completo<input required minLength={2} maxLength={120} value={form.customerName} onChange={(event) => updateField("customerName", event.target.value)} autoComplete="name" /></label>
              <label>E-mail<input required type="email" maxLength={180} value={form.email} onChange={(event) => updateField("email", event.target.value)} autoComplete="email" /></label>
              <label>Telefone<input required minLength={8} maxLength={30} value={form.phone} onChange={(event) => updateField("phone", event.target.value)} autoComplete="tel" /></label>
              <label>Modalidade<select value={form.shippingMethod} onChange={(event) => updateField("shippingMethod", event.target.value as ShippingMethod)}><option value="PAC">PAC</option><option value="SEDEX">SEDEX</option><option value="RETIRADA">Retirada</option></select></label>
              <label>CEP<input required={requiresAddress} maxLength={20} value={form.zipCode} onChange={(event) => updateField("zipCode", event.target.value)} autoComplete="postal-code" /></label>
              <label>Endereço<input required={requiresAddress} maxLength={120} value={form.address} onChange={(event) => updateField("address", event.target.value)} autoComplete="street-address" /></label>
              <label>Número<input required={requiresAddress} maxLength={20} value={form.number} onChange={(event) => updateField("number", event.target.value)} autoComplete="address-line2" /></label>
              <label>Complemento <span className="checkout-label-note">(opcional)</span><input maxLength={80} value={form.complement} onChange={(event) => updateField("complement", event.target.value)} autoComplete="address-line2" /></label>
              <label>Bairro<input required={requiresAddress} maxLength={80} value={form.neighborhood} onChange={(event) => updateField("neighborhood", event.target.value)} autoComplete="address-level3" /></label>
              <label>Cidade<input required={requiresAddress} maxLength={80} value={form.city} onChange={(event) => updateField("city", event.target.value)} autoComplete="address-level2" /></label>
              <label>Estado<input required={requiresAddress} maxLength={2} value={form.state} onChange={(event) => updateField("state", event.target.value.toUpperCase())} autoComplete="address-level1" /></label>
            </div>
            <p className="checkout-hint">O frete e o total definitivo são calculados e validados pelo servidor. O pedido ficará pendente; nenhum pagamento será iniciado.</p>
            <button className="cart-button" type="submit" disabled={submitting || !quote}>{submitting ? "Preparando pedido..." : "Finalizar pedido"}</button>
            <p className="cart-note">{submitting ? "Não feche esta página. O mesmo envio será reutilizado se for necessário tentar novamente." : "Você poderá revisar o status do pedido após o envio."}</p>
          </form>
          <aside className="checkout-summary">
            <p className="eyebrow">Sua seleção</p><h2>Resumo</h2>
            <div className="checkout-summary__items">
              {cart.items.map((item) => {
                const image = resolveMediaUrl(item.imageUrl);
                return <div className="checkout-summary__item" key={item.productId}><div className="checkout-summary__image">{image ? (// eslint-disable-next-line @next/next/no-img-element
                  <img src={image} alt="" />) : <span>Angel</span>}</div><div><strong>{item.name}</strong><span>{item.quantity} × {currency.format(item.unitPrice)}</span></div><b>{currency.format(item.subtotal)}</b></div>;
              })}
            </div>
            <div className="checkout-summary__line"><span>Subtotal</span><strong>{currency.format(quote?.subtotal ?? cart.subtotal)}</strong></div>
            <div className="checkout-summary__line"><span>Frete</span><strong>{quote ? (quote.shippingCost === 0 ? "Grátis" : currency.format(quote.shippingCost)) : (quoteError ?? "Calculando...")}</strong></div>
            <div className="checkout-summary__total"><span>Total</span><strong>{quote ? currency.format(quote.total) : "Indisponível"}</strong></div>
          </aside>
        </div>
      )}
    </section>
  );
}
