"use client";

import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { fetchCart } from "@/lib/cart-client";
import { clearStoredCart } from "@/lib/cart-storage";
import { resolveMediaUrl } from "@/lib/api";
import type { CartResponse } from "@/lib/cart-types";
import { emptyCart } from "@/lib/cart-types";
import { getOrderQuote, OrderApiError, OrderSubmissionUncertainError, prepareOrder } from "@/lib/order-client";
import { fetchAuthorizedOrders } from "@/lib/order-tracking-client";
import type { CreateOrderRequest, OrderQuoteResponse, OrderResponse, ShippingMethod } from "@/lib/order-types";
import { formatCep, lookupCep } from "@/lib/cep";
import { validateCheckout, type CheckoutValidationErrors } from "@/lib/checkout-validation";

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const checkoutStorageKey = "angel-checkout-order-v1";
const maxBackendAddressLength = 240;

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
    if (error.code === "invalid_zip_code") return "Informe um CEP brasileiro válido com 8 dígitos.";
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
  const [fieldErrors, setFieldErrors] = useState<CheckoutValidationErrors>({});
  const [cepStatus, setCepStatus] = useState<"idle" | "loading" | "success" | "error">("idle");
  const [idempotencyKey, setIdempotencyKey] = useState(() => readStoredOrder()?.key ?? "");
  const [cartLoadAttempt, setCartLoadAttempt] = useState(0);
  const cepRequest = useRef(0);
  const requiresAddress = form.shippingMethod !== "RETIRADA";

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
    const orderId = new URLSearchParams(window.location.search).get("orderId");
    if (!orderId) return;
    fetchAuthorizedOrders().then((orders) => {
      const returnedOrder = orders.find((candidate) => String(candidate.id) === orderId);
      if (returnedOrder) setOrder(returnedOrder);
    }).catch(() => undefined);
  }, []);

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

  useEffect(() => {
    const digits = form.zipCode.replace(/\D/g, "");
    const requestId = ++cepRequest.current;
    const controller = new AbortController();

    if (!requiresAddress || digits.length !== 8) {
      setCepStatus("idle");
      return () => controller.abort();
    }

    setCepStatus("loading");
    lookupCep(digits, controller.signal)
      .then((address) => {
        if (requestId !== cepRequest.current) return;
        setForm((current) => ({
          ...current,
          address: address.street || current.address,
          neighborhood: address.neighborhood || current.neighborhood,
          city: address.city || current.city,
          state: address.state || current.state,
        }));
        setFieldErrors((current) => {
          const next = { ...current };
          delete next.cep;
          delete next.address;
          delete next.neighborhood;
          delete next.city;
          delete next.state;
          return next;
        });
        setCepStatus("success");
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || requestId !== cepRequest.current) return;
        setCepStatus("error");
        setFieldErrors((current) => ({
          ...current,
          cep: error instanceof Error ? error.message : "Não foi possível consultar o CEP. Preencha o endereço manualmente.",
        }));
      });

    return () => controller.abort();
  }, [form.zipCode, requiresAddress]);

  function updateField(field: keyof CheckoutForm, value: string) {
    if (field === "shippingMethod") {
      setQuote(null);
      setQuoteError(null);
    }
    setFieldErrors((current) => {
      if (!current[field]) return current;
      const next = { ...current };
      delete next[field];
      return next;
    });
    if (field === "zipCode") value = formatCep(value);
    setForm((current) => ({ ...current, [field]: value }));
  }

  function validateForm() {
    const errors = validateCheckout({ ...form, requiresAddress });
    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  }

  function retryCartLoad() {
    setCartError(null);
    setLoading(true);
    setCartLoadAttempt((attempt) => attempt + 1);
  }

  async function submitOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || order) return;
    if (!validateForm()) {
      setOrderError("Confira os campos destacados antes de continuar.");
      return;
    }
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
      const formattedAddress = addressParts.join(" | ");
      if (formattedAddress.length > maxBackendAddressLength) {
        setOrderError("O endereço completo deve ter no máximo 240 caracteres. Reduza os detalhes do endereço e tente novamente.");
        return;
      }
      const request: CreateOrderRequest = {
        customerName: form.customerName.trim(), email: form.email.trim(), phone: form.phone.trim(),
        zipCode: form.zipCode.trim(), address: formattedAddress,
        addressNumber: requiresAddress ? form.number.trim() : undefined,
        addressComplement: requiresAddress ? form.complement.trim() : undefined,
        neighborhood: requiresAddress ? form.neighborhood.trim() : undefined,
        city: requiresAddress ? form.city.trim() : undefined,
        state: requiresAddress ? form.state.trim().toUpperCase() : undefined,
        shippingMethod: form.shippingMethod,
      };

      const created = await prepareOrder(request, csrfToken, key);
      if (!created.checkoutUrl) throw new Error("O Mercado Pago não retornou uma URL de checkout.");
      sessionStorage.setItem(checkoutStorageKey, JSON.stringify({ key, order: created }));
      setOrder(created);
      setCart(emptyCart);
      clearStoredCart();
      window.dispatchEvent(new Event("angel:cart-updated"));
      window.location.assign(created.checkoutUrl);
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
          <h2>{order.paymentStatus === "Aprovado" ? "Pagamento recebido" : "Pedido preparado"}</h2>
          <p>{order.paymentStatus === "Aprovado" ? "Pagamento confirmado pelo Mercado Pago." : "Seu pedido está aguardando a confirmação do Mercado Pago."}</p>
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
            <div className="checkout-fields" aria-live="polite">
              <label htmlFor="checkout-name">Nome completo<input id="checkout-name" required minLength={2} maxLength={120} value={form.customerName} onChange={(event) => updateField("customerName", event.target.value)} autoComplete="name" aria-invalid={Boolean(fieldErrors.customerName)} aria-describedby={fieldErrors.customerName ? "checkout-name-error" : undefined} />{fieldErrors.customerName && <span id="checkout-name-error" className="checkout-field-error" role="alert">{fieldErrors.customerName}</span>}</label>
              <label htmlFor="checkout-email">E-mail<input id="checkout-email" required type="email" maxLength={180} value={form.email} onChange={(event) => updateField("email", event.target.value)} autoComplete="email" inputMode="email" aria-invalid={Boolean(fieldErrors.email)} aria-describedby={fieldErrors.email ? "checkout-email-error" : undefined} />{fieldErrors.email && <span id="checkout-email-error" className="checkout-field-error" role="alert">{fieldErrors.email}</span>}</label>
              <label htmlFor="checkout-phone">Telefone<input id="checkout-phone" required type="tel" minLength={8} maxLength={30} value={form.phone} onChange={(event) => updateField("phone", event.target.value)} autoComplete="tel" inputMode="tel" aria-invalid={Boolean(fieldErrors.phone)} aria-describedby={fieldErrors.phone ? "checkout-phone-error" : undefined} />{fieldErrors.phone && <span id="checkout-phone-error" className="checkout-field-error" role="alert">{fieldErrors.phone}</span>}</label>
              <label>Modalidade<select value={form.shippingMethod} onChange={(event) => updateField("shippingMethod", event.target.value as ShippingMethod)}><option value="PAC">PAC</option><option value="SEDEX">SEDEX</option><option value="RETIRADA">Retirada</option></select></label>
              <label htmlFor="checkout-cep">CEP<input id="checkout-cep" required={requiresAddress} maxLength={9} value={form.zipCode} onChange={(event) => updateField("zipCode", event.target.value)} autoComplete="postal-code" inputMode="numeric" aria-invalid={Boolean(fieldErrors.cep)} aria-describedby={fieldErrors.cep ? "checkout-cep-error" : "checkout-cep-status"} />{cepStatus === "loading" && <span id="checkout-cep-status" className="checkout-field-note" role="status">Consultando endereço…</span>}{cepStatus === "success" && <span id="checkout-cep-status" className="checkout-field-note" role="status">Endereço preenchido. Confira e corrija se necessário.</span>}{fieldErrors.cep && <span id="checkout-cep-error" className="checkout-field-error" role="alert">{fieldErrors.cep}</span>}</label>
              <label htmlFor="checkout-address">Endereço<input id="checkout-address" required={requiresAddress} maxLength={120} value={form.address} onChange={(event) => updateField("address", event.target.value)} autoComplete="address-line1" aria-invalid={Boolean(fieldErrors.address)} aria-describedby={fieldErrors.address ? "checkout-address-error" : undefined} />{fieldErrors.address && <span id="checkout-address-error" className="checkout-field-error" role="alert">{fieldErrors.address}</span>}</label>
              <label htmlFor="checkout-number">Número<input id="checkout-number" required={requiresAddress} maxLength={20} value={form.number} onChange={(event) => updateField("number", event.target.value)} inputMode="numeric" autoComplete="off" aria-invalid={Boolean(fieldErrors.number)} aria-describedby={fieldErrors.number ? "checkout-number-error" : undefined} />{fieldErrors.number && <span id="checkout-number-error" className="checkout-field-error" role="alert">{fieldErrors.number}</span>}</label>
              <label htmlFor="checkout-complement">Complemento <span className="checkout-label-note">(opcional)</span><input id="checkout-complement" maxLength={80} value={form.complement} onChange={(event) => updateField("complement", event.target.value)} autoComplete="address-line2" /></label>
              <label htmlFor="checkout-neighborhood">Bairro<input id="checkout-neighborhood" required={requiresAddress} maxLength={80} value={form.neighborhood} onChange={(event) => updateField("neighborhood", event.target.value)} autoComplete="address-level3" aria-invalid={Boolean(fieldErrors.neighborhood)} aria-describedby={fieldErrors.neighborhood ? "checkout-neighborhood-error" : undefined} />{fieldErrors.neighborhood && <span id="checkout-neighborhood-error" className="checkout-field-error" role="alert">{fieldErrors.neighborhood}</span>}</label>
              <label htmlFor="checkout-city">Cidade<input id="checkout-city" required={requiresAddress} maxLength={80} value={form.city} onChange={(event) => updateField("city", event.target.value)} autoComplete="address-level2" aria-invalid={Boolean(fieldErrors.city)} aria-describedby={fieldErrors.city ? "checkout-city-error" : undefined} />{fieldErrors.city && <span id="checkout-city-error" className="checkout-field-error" role="alert">{fieldErrors.city}</span>}</label>
              <label htmlFor="checkout-state">Estado<input id="checkout-state" required={requiresAddress} maxLength={2} value={form.state} onChange={(event) => updateField("state", event.target.value.toUpperCase())} autoComplete="address-level1" aria-invalid={Boolean(fieldErrors.state)} aria-describedby={fieldErrors.state ? "checkout-state-error" : undefined} />{fieldErrors.state && <span id="checkout-state-error" className="checkout-field-error" role="alert">{fieldErrors.state}</span>}</label>
            </div>
            <p className="checkout-hint">O valor exibido usa a tabela comercial atual da loja e é validado pelo servidor. O pedido ficará pendente; nenhum pagamento será iniciado.</p>
            <button className="cart-button" type="submit" disabled={submitting || !quote}>{submitting ? "Abrindo Mercado Pago..." : "Pagar com Mercado Pago"}</button>
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
            <div className="checkout-summary__line"><span>Frete (tabela atual)</span><strong>{quote ? (quote.shippingCost === 0 ? "Grátis" : currency.format(quote.shippingCost)) : (quoteError ?? "Calculando...")}</strong></div>
            <div className="checkout-summary__total"><span>Total</span><strong>{quote ? currency.format(quote.total) : "Indisponível"}</strong></div>
          </aside>
        </div>
      )}
    </section>
  );
}
