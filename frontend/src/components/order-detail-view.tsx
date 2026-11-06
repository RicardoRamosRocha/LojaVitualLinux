"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import type { OrderResponse } from "@/lib/order-types";
import { fetchAuthorizedOrders, cancelOrder, OrderTrackingApiError } from "@/lib/order-tracking-client";
import { fetchCart } from "@/lib/cart-client";
import { resumeOrderPayment } from "@/lib/order-client";

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

function formatDate(value?: string) {
  if (!value) return "Data não disponível";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Data não disponível" : new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium" }).format(date);
}

function paymentStatusMessage(status?: string) {
  switch (status) {
    case "Aprovado": return "Pagamento confirmado pelo Mercado Pago.";
    case "AprovadoAguardandoEstoque": return "Pagamento aprovado; aguardando a confirmação do estoque.";
    case "EmProcessamento": return "O pagamento está em processamento no Mercado Pago.";
    case "Recusado": return "O pagamento foi recusado pelo Mercado Pago.";
    case "Cancelado": return "O pagamento foi cancelado.";
    case "Expirado": return "O prazo para pagamento expirou.";
    case "Reembolsado": return "O pagamento foi reembolsado.";
    default: return "Este pedido está pendente e ainda não representa pagamento confirmado.";
  }
}

type TimelineState = "complete" | "current" | "future" | "blocked";

const operationalStages = [
  { status: "Em preparação", label: "Em preparação", description: "Seu pedido está sendo separado e preparado." },
  { status: "Enviado", label: "Enviado", description: "Avisaremos quando o pedido seguir para transporte." },
  { status: "Entregue", label: "Entregue", description: "A entrega será concluída no endereço informado." },
] as const;

function paymentConfirmed(status?: string) {
  return status === "Aprovado" || status === "AprovadoAguardandoEstoque";
}

function timelineState(order: OrderResponse, index: number): TimelineState {
  const currentIndex = operationalStages.findIndex((stage) => stage.status === order.status);
  const blocked = order.status === "Cancelado" || ["Recusado", "Cancelado", "Expirado", "Reembolsado"].includes(order.paymentStatus ?? "");
  if (blocked) return "blocked";
  if (currentIndex < 0) return "future";
  if (index < currentIndex) return "complete";
  if (index === currentIndex) return "current";
  return "future";
}

export function OrderDetailView({ reference }: { reference: string }) {
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [resumingPayment, setResumingPayment] = useState(false);
  const [paymentError, setPaymentError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [cancellationMessage, setCancellationMessage] = useState<string | null>(null);
  const [cancellationError, setCancellationError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  async function handleResumePayment() {
    if (!order || resumingPayment) return;
    setResumingPayment(true);
    setPaymentError(null);
    try {
      const { csrfToken } = await fetchCart();
      if (!csrfToken) throw new Error("Não foi possível validar sua sessão. Recarregue a página e tente novamente.");
      const resumed = await resumeOrderPayment(order.id, csrfToken);
      setOrder(resumed);
      if (!resumed.checkoutUrl) throw new Error("O Mercado Pago não retornou uma URL de checkout.");
      window.location.assign(resumed.checkoutUrl);
    } catch (caughtError) {
      setPaymentError(caughtError instanceof Error ? caughtError.message : "Não foi possível retomar o pagamento.");
    } finally {
      setResumingPayment(false);
    }
  }

  async function handleCancelOrder() {
    if (!order || cancelling || order.status !== "Pendente") return;
    if (!window.confirm("Cancelar este pedido? O pagamento será verificado antes da liberação do estoque.")) return;

    setCancelling(true);
    setCancellationMessage(null);
    setCancellationError(null);
    try {
      const { csrfToken } = await fetchCart();
      if (!csrfToken) throw new Error("Não foi possível validar sua sessão. Recarregue a página e tente novamente.");
      const cancelled = await cancelOrder(order.id, csrfToken);
      setOrder(cancelled);
      setCancellationMessage("Pedido cancelado. O histórico financeiro foi preservado.");
    } catch (caughtError) {
      setCancellationError(caughtError instanceof Error ? caughtError.message : "Não foi possível cancelar o pedido.");
    } finally {
      setCancelling(false);
    }
  }

  const canPay = order?.status === "Pendente" &&
    ["CriandoPreferencia", "AguardandoPagamento"].includes(order.paymentStatus ?? "");

  const loadOrder = useCallback((showLoading = true) => {
    if (showLoading) setLoading(true);
    setError(false);
    return fetchAuthorizedOrders()
      .then((orders) => setOrder(orders.find((item) => String(item.id) === reference) ?? null))
      .catch((caughtError) => {
        if (!(caughtError instanceof OrderTrackingApiError && caughtError.status === 401)) setError(true);
        setOrder(null);
      })
      .finally(() => { if (showLoading) setLoading(false); });
  }, [reference]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadOrder(), 0);
    return () => window.clearTimeout(timer);
  }, [loadOrder]);

  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState !== "visible") return;
      setRefreshing(true);
      void loadOrder(false).finally(() => setRefreshing(false));
    };
    const interval = window.setInterval(refresh, 30_000);
    document.addEventListener("visibilitychange", refresh);
    return () => {
      window.clearInterval(interval);
      document.removeEventListener("visibilitychange", refresh);
    };
  }, [loadOrder]);

  if (loading) return <div className="cart-message" role="status">Carregando detalhes do pedido...</div>;
  if (error) return <div className="cart-message cart-message--error" role="alert"><h2>Não conseguimos carregar este pedido.</h2><p>Tente novamente em instantes.</p><button className="cart-button cart-button--retry" type="button" onClick={() => void loadOrder()}>Tentar novamente</button></div>;
  if (!order) return <div className="cart-message"><span className="catalog-message__eyebrow">Acesso não encontrado</span><h2>Este pedido não está autorizado neste dispositivo.</h2><p>Abra um pedido a partir de “Meus pedidos” ou continue comprando.</p><Link className="hero-cta" href="/pedidos">Voltar para meus pedidos <span>↗</span></Link></div>;

  return (
    <section className="order-detail-shell">
      <div className="orders-heading"><div><p className="eyebrow">Pedido #{order.id}</p><h1>Acompanhamento</h1></div><div><button className="back-link" type="button" onClick={() => { setRefreshing(true); void loadOrder(false).finally(() => setRefreshing(false)); }} disabled={refreshing}>{refreshing ? "Atualizando..." : "Atualizar"}</button><Link className="back-link" href="/pedidos">← Meus pedidos</Link></div></div>
      <div className="order-detail-layout">
        <p className="order-note">Status financeiro: {order.paymentStatus ?? "Aguardando confirmação"}</p>
        {order.status === "Pendente" && <div className="order-payment-action"><button className="back-link" type="button" onClick={handleCancelOrder} disabled={cancelling}>{cancelling ? "Verificando pagamento..." : "Cancelar pedido"}</button>{cancellationMessage && <p className="order-note" role="status">{cancellationMessage}</p>}{cancellationError && <p className="order-note" role="alert">{cancellationError}</p>}</div>}
        {canPay && <div className="order-payment-action"><button className="cart-button" type="button" onClick={handleResumePayment} disabled={resumingPayment}>{resumingPayment ? "Abrindo Mercado Pago..." : "Pagar agora"}</button>{paymentError && <p className="order-note" role="alert">{paymentError}</p>}</div>}
        <div className="order-detail-card"><div className="order-detail-card__header"><div><p className="eyebrow">Resumo</p><h2>Pedido #{order.id}</h2></div><strong>{order.status}</strong></div><div className="order-items">{order.items.map((item) => <div className="order-item" key={item.productId}><span>{item.quantity} × Produto #{item.productId}</span><strong>{currency.format(item.subtotal)}</strong></div>)}</div><div className="order-total-line"><span>Subtotal</span><strong>{currency.format(order.subtotal)}</strong></div><div className="order-total-line"><span>Frete</span><strong>{currency.format(order.shippingCost)}</strong></div><div className="order-total-line order-total-line--final"><span>Total</span><strong>{currency.format(order.total)}</strong></div></div>
        <aside className="order-timeline" aria-label="Linha do tempo do pedido"><p className="eyebrow">Acompanhamento</p><div className={`order-timeline__step is-${paymentConfirmed(order.paymentStatus) ? "complete" : ["Recusado", "Cancelado", "Expirado", "Reembolsado"].includes(order.paymentStatus ?? "") || order.status === "Cancelado" ? "blocked" : "current"}`} aria-current={!paymentConfirmed(order.paymentStatus) ? "step" : undefined}><span aria-hidden="true" /><div><strong>Pagamento confirmado</strong><small>{paymentConfirmed(order.paymentStatus) ? `Pedido registrado em ${formatDate(order.createdAt)}.` : paymentStatusMessage(order.paymentStatus)}</small></div></div>{operationalStages.map((stage, index) => { const state = timelineState(order, index); return <div className={`order-timeline__step is-${state}`} key={stage.status} aria-current={state === "current" ? "step" : undefined}><span aria-hidden="true" /><div><strong>{stage.label}</strong><small>{state === "blocked" ? "Esta etapa não será iniciada para este pedido." : state === "complete" ? "Etapa concluída." : state === "current" ? `Status atual: ${order.status}.` : stage.description}</small></div></div>; })}<p className="order-note">{paymentStatusMessage(order.paymentStatus)}</p></aside>
      </div>
    </section>
  );
}
