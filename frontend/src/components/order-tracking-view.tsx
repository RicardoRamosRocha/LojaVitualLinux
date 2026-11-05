"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import type { OrderResponse } from "@/lib/order-types";
import { fetchCart } from "@/lib/cart-client";
import { confirmOrderRecovery, fetchAuthorizedOrders, OrderTrackingApiError, requestOrderRecovery } from "@/lib/order-tracking-client";

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
  const [unauthorized, setUnauthorized] = useState(false);
  const [showCancelled, setShowCancelled] = useState(false);
  const [recoveryOpen, setRecoveryOpen] = useState(false);
  const [recoveryEmail, setRecoveryEmail] = useState("");
  const [recoveryError, setRecoveryError] = useState<string | null>(null);
  const [recoveryMessage, setRecoveryMessage] = useState<string | null>(null);
  const [recoveryLoading, setRecoveryLoading] = useState(false);
  const recoveryLinkHandled = useRef(false);

  const loadOrders = useCallback(() => {
    setLoading(true);
    setError(false);
    setUnauthorized(false);
    fetchAuthorizedOrders()
      .then(setOrders)
      .catch((caughtError) => {
        if (caughtError instanceof OrderTrackingApiError && caughtError.status === 401) setUnauthorized(true);
        else setError(true);
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
        if (caughtError instanceof OrderTrackingApiError && caughtError.status === 401) setUnauthorized(true);
        else setError(true);
        setOrders([]);
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (recoveryLinkHandled.current) return;
    const params = new URLSearchParams(window.location.search);
    const orderId = Number(params.get("recoveryOrderId"));
    const token = params.get("recoveryToken");
    if (!Number.isInteger(orderId) || orderId <= 0 || !token) return;

    recoveryLinkHandled.current = true;
    queueMicrotask(() => {
      setRecoveryOpen(true);
      setRecoveryLoading(true);
      setRecoveryError(null);
      setRecoveryMessage("Validando seu link de recuperação...");
      fetchCart()
        .then(({ csrfToken }) => {
          if (!csrfToken) throw new Error("Não foi possível validar sua sessão. Recarregue a página.");
          return confirmOrderRecovery(orderId, token, csrfToken);
        })
        .then(() => {
          window.history.replaceState({}, document.title, "/pedidos");
          setRecoveryMessage("Acesso recuperado. Seus pedidos autorizados foram carregados.");
          loadOrders();
        })
        .catch((caughtError) => {
          setRecoveryError(caughtError instanceof OrderTrackingApiError && caughtError.status === 401
            ? "O link é inválido, expirou ou já foi utilizado."
            : caughtError instanceof Error ? caughtError.message : "Não foi possível confirmar o link de recuperação.");
        })
        .finally(() => setRecoveryLoading(false));
    });
  }, [loadOrders]);

  function openRecovery() {
    setRecoveryOpen(true);
    setRecoveryError(null);
    setRecoveryMessage(null);
  }

  async function submitRecoveryRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setRecoveryLoading(true);
    setRecoveryError(null);
    setRecoveryMessage(null);
    try {
      const { csrfToken } = await fetchCart();
      if (!csrfToken) throw new Error("Não foi possível validar sua sessão. Recarregue a página.");
      await requestOrderRecovery(recoveryEmail, csrfToken);
      setRecoveryMessage("Se os dados corresponderem, enviaremos um link temporário para o e-mail da compra.");
    } catch (caughtError) {
      setRecoveryError(caughtError instanceof Error ? caughtError.message : "Não foi possível iniciar a recuperação.");
    } finally { setRecoveryLoading(false); }
  }

  const visibleOrders = showCancelled ? orders : orders.filter((order) => order.status !== "Cancelado");
  const cancelledCount = orders.filter((order) => order.status === "Cancelado").length;

  return (
    <section className="orders-shell">
      <div className="orders-heading">
        <div><p className="eyebrow">Acompanhe de perto</p><h1>Meus pedidos</h1></div>
        <button className="hero-cta" type="button" onClick={openRecovery}>Recuperar meus pedidos <span>↗</span></button>
        <Link className="back-link" href="/#colecao">← Continuar comprando</Link>
      </div>
      {loading && <div className="cart-message" role="status" aria-live="polite">Carregando seus pedidos...</div>}
      {!loading && error && <div className="cart-message cart-message--error" role="alert"><h2>Não conseguimos carregar seus pedidos.</h2><p>Verifique sua conexão e tente novamente.</p><button className="cart-button cart-button--retry" type="button" onClick={loadOrders}>Tentar novamente</button></div>}
      {!loading && !error && unauthorized && <div className="cart-message" role="status"><span className="catalog-message__eyebrow">Sessão sem autorização</span><h2>Esta sessão não possui acesso aos pedidos anteriores.</h2><p>Recupere o acesso comprovando a posse do e-mail usado na compra.</p></div>}
      {!loading && !error && recoveryOpen && <div className="cart-message" role="dialog" aria-labelledby="recovery-title">
        <h2 id="recovery-title">Recuperar meus pedidos</h2>
        <form onSubmit={submitRecoveryRequest}>
          <label htmlFor="recovery-email">E-mail usado na compra</label>
          <input id="recovery-email" type="email" required value={recoveryEmail} onChange={(event) => setRecoveryEmail(event.target.value)} />
          <button className="cart-button" type="submit" disabled={recoveryLoading}>{recoveryLoading ? "Enviando..." : "Enviar link de acesso"}</button>
        </form>
        {recoveryMessage && <p className="order-note" role="status">{recoveryMessage}</p>}
        {recoveryError && <p className="order-note" role="alert">{recoveryError}</p>}
        <button className="back-link" type="button" onClick={() => setRecoveryOpen(false)}>Fechar</button>
      </div>}
      {!loading && !error && !unauthorized && orders.length > 0 && <div className="orders-history-toggle"><span>{showCancelled ? "Histórico completo" : "Pedidos ativos"}</span>{cancelledCount > 0 && <button className="back-link" type="button" onClick={() => setShowCancelled((current) => !current)}>{showCancelled ? "Ocultar cancelados" : `Ver ${cancelledCount} cancelado${cancelledCount === 1 ? "" : "s"}`}</button>}</div>}
      {!loading && !error && !unauthorized && visibleOrders.length === 0 && <div className="cart-message"><span className="catalog-message__eyebrow">{orders.length > 0 ? "Nenhum pedido ativo" : "Ainda não há pedidos aqui"}</span><h2>{orders.length > 0 ? "Seus pedidos cancelados ficam no histórico." : "Seus pedidos autorizados aparecerão neste espaço."}</h2><p>{orders.length > 0 ? "Abra o histórico para consultar os detalhes preservados." : "Finalize uma compra para acompanhar seus itens com segurança neste dispositivo."}</p>{orders.length > 0 ? <button className="hero-cta" type="button" onClick={() => setShowCancelled(true)}>Ver histórico <span>↗</span></button> : <Link className="hero-cta" href="/#colecao">Continuar comprando <span>↗</span></Link>}</div>}
      {!loading && !error && !unauthorized && visibleOrders.length > 0 && <div className="orders-list">{visibleOrders.map((order) => <article className="order-card" key={order.id}><div className="order-card__meta"><p className="eyebrow">Pedido #{order.id}</p><span>{formatDate(order.createdAt)}</span></div><div className="order-card__status"><span>{order.status || "Status não informado"}</span><strong>{currency.format(order.total)}</strong></div><Link className="cart-button cart-button--link" href={`/pedidos/${order.id}`}>Ver detalhes</Link></article>)}</div>}
    </section>
  );
}
