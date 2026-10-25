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

export function OrderTrackingView() {
  const [orders, setOrders] = useState<OrderResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  const loadOrders = useCallback(() => {
    setLoading(true);
    setError(false);
    fetchAuthorizedOrders()
      .then(setOrders)
      .catch((caughtError) => {
        if (!(caughtError instanceof OrderTrackingApiError && caughtError.status === 401)) setError(true);
        setOrders([]);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    let active = true;
    fetchAuthorizedOrders()
      .then((nextOrders) => { if (active) setOrders(nextOrders); })
      .catch((caughtError) => {
        if (!active) return;
        if (!(caughtError instanceof OrderTrackingApiError && caughtError.status === 401)) setError(true);
        setOrders([]);
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  return (
    <section className="orders-shell">
      <div className="orders-heading">
        <div><p className="eyebrow">Acompanhe de perto</p><h1>Meus pedidos</h1></div>
        <Link className="back-link" href="/#colecao">← Continuar comprando</Link>
      </div>
      {loading && <div className="cart-message" role="status" aria-live="polite">Carregando seus pedidos...</div>}
      {!loading && error && <div className="cart-message cart-message--error" role="alert"><h2>Não conseguimos carregar seus pedidos.</h2><p>Verifique sua conexão e tente novamente.</p><button className="cart-button cart-button--retry" type="button" onClick={loadOrders}>Tentar novamente</button></div>}
      {!loading && !error && orders.length === 0 && <div className="cart-message"><span className="catalog-message__eyebrow">Ainda não há pedidos aqui</span><h2>Seus pedidos autorizados aparecerão neste espaço.</h2><p>Finalize uma compra para acompanhar seus itens com segurança neste dispositivo.</p><Link className="hero-cta" href="/#colecao">Continuar comprando <span>↗</span></Link></div>}
      {!loading && !error && orders.length > 0 && <div className="orders-list">{orders.map((order) => <article className="order-card" key={order.id}><div className="order-card__meta"><p className="eyebrow">Pedido #{order.id}</p><span>{formatDate(order.createdAt)}</span></div><div className="order-card__status"><span>{order.status || "Status não informado"}</span><strong>{currency.format(order.total)}</strong></div><Link className="cart-button cart-button--link" href={`/pedidos/${order.id}`}>Ver detalhes</Link></article>)}</div>}
    </section>
  );
}
