"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import type { OrderResponse } from "@/lib/order-types";
import { fetchAuthorizedOrders, OrderTrackingApiError } from "@/lib/order-tracking-client";

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

function formatDate(value?: string) {
  if (!value) return "Data não disponível";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Data não disponível" : new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium" }).format(date);
}

export function OrderDetailView({ reference }: { reference: string }) {
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  const loadOrder = useCallback(() => {
    setLoading(true);
    setError(false);
    fetchAuthorizedOrders()
      .then((orders) => setOrder(orders.find((item) => String(item.id) === reference) ?? null))
      .catch((caughtError) => {
        if (!(caughtError instanceof OrderTrackingApiError && caughtError.status === 401)) setError(true);
        setOrder(null);
      })
      .finally(() => setLoading(false));
  }, [reference]);

  useEffect(() => {
    let active = true;
    fetchAuthorizedOrders()
      .then((orders) => { if (active) setOrder(orders.find((item) => String(item.id) === reference) ?? null); })
      .catch((caughtError) => {
        if (!active) return;
        if (!(caughtError instanceof OrderTrackingApiError && caughtError.status === 401)) setError(true);
        setOrder(null);
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [reference]);
  if (loading) return <div className="cart-message" role="status">Carregando detalhes do pedido...</div>;
  if (error) return <div className="cart-message cart-message--error" role="alert"><h2>Não conseguimos carregar este pedido.</h2><p>Tente novamente em instantes.</p><button className="cart-button cart-button--retry" type="button" onClick={loadOrder}>Tentar novamente</button></div>;
  if (!order) return <div className="cart-message"><span className="catalog-message__eyebrow">Acesso não encontrado</span><h2>Este pedido não está autorizado neste dispositivo.</h2><p>Abra um pedido a partir de “Meus pedidos” ou continue comprando.</p><Link className="hero-cta" href="/pedidos">Voltar para meus pedidos <span>↗</span></Link></div>;

  return (
    <section className="order-detail-shell">
      <div className="orders-heading"><div><p className="eyebrow">Pedido #{order.id}</p><h1>Acompanhamento</h1></div><Link className="back-link" href="/pedidos">← Meus pedidos</Link></div>
      <div className="order-detail-layout">
        <div className="order-detail-card"><div className="order-detail-card__header"><div><p className="eyebrow">Resumo</p><h2>Pedido #{order.id}</h2></div><strong>{order.status}</strong></div><div className="order-items">{order.items.map((item) => <div className="order-item" key={item.productId}><span>{item.quantity} × Produto #{item.productId}</span><strong>{currency.format(item.subtotal)}</strong></div>)}</div><div className="order-total-line"><span>Subtotal</span><strong>{currency.format(order.subtotal)}</strong></div><div className="order-total-line"><span>Frete</span><strong>{currency.format(order.shippingCost)}</strong></div><div className="order-total-line order-total-line--final"><span>Total</span><strong>{currency.format(order.total)}</strong></div></div>
        <aside className="order-timeline" aria-label="Linha do tempo do pedido"><p className="eyebrow">Acompanhamento</p><div className="order-timeline__step is-current"><span aria-hidden="true" /><div><strong>Status atual: {order.status}</strong><small>Pedido registrado em {formatDate(order.createdAt)}.</small></div></div><div className="order-timeline__step"><span aria-hidden="true" /><div><strong>Próximas atualizações</strong><small>Serão exibidas aqui quando disponibilizadas pela API.</small></div></div><p className="order-note">Este pedido está pendente e não representa pagamento confirmado.</p></aside>
      </div>
    </section>
  );
}
