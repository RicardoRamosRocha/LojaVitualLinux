"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
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
  const [recoveryOpen, setRecoveryOpen] = useState(false);
  const [recoveryStage, setRecoveryStage] = useState<"request" | "confirm">("request");
  const [recoveryOrderId, setRecoveryOrderId] = useState("");
  const [recoveryEmail, setRecoveryEmail] = useState("");
  const [recoveryToken, setRecoveryToken] = useState("");
  const [recoveryError, setRecoveryError] = useState<string | null>(null);
  const [recoveryMessage, setRecoveryMessage] = useState<string | null>(null);
  const [recoveryLoading, setRecoveryLoading] = useState(false);

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

  async function submitRecoveryRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setRecoveryLoading(true);
    setRecoveryError(null);
    setRecoveryMessage(null);
    try {
      const { csrfToken } = await fetchCart();
      if (!csrfToken) throw new Error("Não foi possível validar sua sessão. Recarregue a página.");
      await requestOrderRecovery(Number(recoveryOrderId), recoveryEmail, csrfToken);
      setRecoveryStage("confirm");
      setRecoveryMessage("Se os dados corresponderem, enviaremos um código para o e-mail do pedido.");
    } catch (caughtError) {
      setRecoveryError(caughtError instanceof Error ? caughtError.message : "Não foi possível iniciar a recuperação.");
    } finally { setRecoveryLoading(false); }
  }

  async function submitRecoveryConfirmation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setRecoveryLoading(true);
    setRecoveryError(null);
    try {
      const { csrfToken } = await fetchCart();
      if (!csrfToken) throw new Error("Não foi possível validar sua sessão. Recarregue a página.");
      await confirmOrderRecovery(Number(recoveryOrderId), recoveryToken, csrfToken);
      setRecoveryOpen(false);
      setRecoveryStage("request");
      setRecoveryToken("");
      loadOrders();
    } catch (caughtError) {
      setRecoveryError(caughtError instanceof OrderTrackingApiError && caughtError.status === 401
        ? "O código é inválido, expirou ou já foi utilizado."
        : caughtError instanceof Error ? caughtError.message : "Não foi possível confirmar o código.");
    } finally { setRecoveryLoading(false); }
  }

  return (
    <section className="orders-shell">
      <div className="orders-heading">
        <div><p className="eyebrow">Acompanhe de perto</p><h1>Meus pedidos</h1></div>
        <Link className="back-link" href="/#colecao">← Continuar comprando</Link>
      </div>
      {loading && <div className="cart-message" role="status" aria-live="polite">Carregando seus pedidos...</div>}
      {!loading && error && <div className="cart-message cart-message--error" role="alert"><h2>Não conseguimos carregar seus pedidos.</h2><p>Verifique sua conexão e tente novamente.</p><button className="cart-button cart-button--retry" type="button" onClick={loadOrders}>Tentar novamente</button></div>}
      {!loading && !error && unauthorized && <div className="cart-message" role="status"><span className="catalog-message__eyebrow">Sessão sem autorização</span><h2>Esta sessão não possui acesso aos pedidos anteriores.</h2><p>Recupere o acesso comprovando a posse do e-mail usado no pedido.</p><button className="hero-cta" type="button" onClick={() => { setRecoveryOpen(true); setRecoveryError(null); setRecoveryMessage(null); }}>Recuperar pedido <span>↗</span></button></div>}
      {!loading && !error && unauthorized && recoveryOpen && <div className="cart-message" role="dialog" aria-labelledby="recovery-title">
        <h2 id="recovery-title">Recuperar pedido</h2>
        {recoveryStage === "request" ? <form onSubmit={submitRecoveryRequest}>
          <label htmlFor="recovery-order-id">Número do pedido</label>
          <input id="recovery-order-id" inputMode="numeric" required value={recoveryOrderId} onChange={(event) => setRecoveryOrderId(event.target.value)} />
          <label htmlFor="recovery-email">E-mail usado na compra</label>
          <input id="recovery-email" type="email" required value={recoveryEmail} onChange={(event) => setRecoveryEmail(event.target.value)} />
          <button className="cart-button" type="submit" disabled={recoveryLoading}>{recoveryLoading ? "Verificando..." : "Enviar código"}</button>
        </form> : <form onSubmit={submitRecoveryConfirmation}>
          <p>Informe o código recebido. Ele é temporário e pode ser usado uma única vez.</p>
          <label htmlFor="recovery-token">Código de recuperação</label>
          <input id="recovery-token" required autoComplete="one-time-code" value={recoveryToken} onChange={(event) => setRecoveryToken(event.target.value)} />
          <button className="cart-button" type="submit" disabled={recoveryLoading}>{recoveryLoading ? "Confirmando..." : "Confirmar acesso"}</button>
        </form>}
        {recoveryMessage && <p className="order-note" role="status">{recoveryMessage}</p>}
        {recoveryError && <p className="order-note" role="alert">{recoveryError}</p>}
      </div>}
      {!loading && !error && !unauthorized && orders.length === 0 && <div className="cart-message"><span className="catalog-message__eyebrow">Ainda não há pedidos aqui</span><h2>Seus pedidos autorizados aparecerão neste espaço.</h2><p>Finalize uma compra para acompanhar seus itens com segurança neste dispositivo.</p><Link className="hero-cta" href="/#colecao">Continuar comprando <span>↗</span></Link></div>}
      {!loading && !error && !unauthorized && orders.length > 0 && <div className="orders-list">{orders.map((order) => <article className="order-card" key={order.id}><div className="order-card__meta"><p className="eyebrow">Pedido #{order.id}</p><span>{formatDate(order.createdAt)}</span></div><div className="order-card__status"><span>{order.status || "Status não informado"}</span><strong>{currency.format(order.total)}</strong></div><Link className="cart-button cart-button--link" href={`/pedidos/${order.id}`}>Ver detalhes</Link></article>)}</div>}
    </section>
  );
}
