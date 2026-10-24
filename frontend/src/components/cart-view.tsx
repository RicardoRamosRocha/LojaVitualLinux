"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { clearCart, fetchCart, removeCartItem, updateCartItem } from "@/lib/cart-client";
import { resolveMediaUrl } from "@/lib/api";
import type { CartResponse } from "@/lib/cart-types";
import { emptyCart } from "@/lib/cart-types";

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

export function CartView() {
  const [cart, setCart] = useState<CartResponse>(emptyCart);
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [pendingId, setPendingId] = useState<number | "clear" | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchCart().then((result) => {
      setCart(result.cart);
      setCsrfToken(result.csrfToken);
    }).catch((caughtError) => {
      setError(caughtError instanceof Error ? caughtError.message : "Não foi possível carregar o carrinho.");
    }).finally(() => setLoading(false));
  }, []);

  async function getCsrfToken() {
    if (csrfToken) return csrfToken;

    const result = await fetchCart();
    setCart(result.cart);
    setCsrfToken(result.csrfToken);
    return result.csrfToken;
  }

  async function changeQuantity(productId: number, quantity: number, availableStock: number) {
    if (quantity < 1 || quantity > availableStock) return;

    setPendingId(productId);
    setError(null);
    try {
      const token = await getCsrfToken();
      if (!token) throw new Error("Não foi possível proteger esta operação.");
      setCart(await updateCartItem(productId, quantity, token));
    }
    catch (caughtError) { setError(caughtError instanceof Error ? caughtError.message : "Não foi possível atualizar o carrinho."); }
    finally { setPendingId(null); }
  }

  async function remove(productId: number) {
    setPendingId(productId); setError(null);
    try {
      const token = await getCsrfToken();
      if (!token) throw new Error("Não foi possível proteger esta operação.");
      setCart(await removeCartItem(productId, token));
    }
    catch (caughtError) { setError(caughtError instanceof Error ? caughtError.message : "Não foi possível remover o item."); }
    finally { setPendingId(null); }
  }

  async function clear() {
    setPendingId("clear"); setError(null);
    try {
      const token = await getCsrfToken();
      if (!token) throw new Error("Não foi possível proteger esta operação.");
      setCart(await clearCart(token));
    }
    catch (caughtError) { setError(caughtError instanceof Error ? caughtError.message : "Não foi possível esvaziar o carrinho."); }
    finally { setPendingId(null); }
  }

  return (
    <section className="cart-shell">
      <div className="cart-heading">
        <div><p className="eyebrow">Sua seleção</p><h1>Meu carrinho</h1></div>
        <Link className="back-link" href="/#colecao">← Continuar comprando</Link>
      </div>
      {loading && <div className="cart-message">Carregando seu carrinho...</div>}
      {error && <div className="cart-message cart-message--error" role="alert">{error}</div>}
      {!loading && !error && cart.items.length === 0 && (
        <div className="cart-message">
          <span className="catalog-message__eyebrow">Por enquanto, nada por aqui</span>
          <h2>Seu carrinho está vazio.</h2>
          <p>Escolha uma peça da coleção para começar.</p>
          <Link className="hero-cta" href="/#colecao">Ver coleção <span>↘</span></Link>
        </div>
      )}
      {!loading && cart.items.length > 0 && (
        <div className="cart-layout">
          <div className="cart-items">
            {cart.items.map((item) => {
              const image = resolveMediaUrl(item.imageUrl);
              const busy = pendingId === item.productId;
              return (
                <article className="cart-item" key={item.productId}>
                  <Link className="cart-item__image-wrap" href={`/produtos/${item.productId}`}>
                    {image ? (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img src={image} alt={item.name} />
                    ) : <span>Angel</span>}
                  </Link>
                  <div className="cart-item__content">
                    <p className="product-card__category">Peça Angel</p>
                    <Link href={`/produtos/${item.productId}`}><h2>{item.name}</h2></Link>
                    <p className="cart-item__unit">{currency.format(item.unitPrice)} cada</p>
                    <div className="cart-item__actions">
                      <div className="quantity-control" aria-label={`Quantidade de ${item.name}`}>
                        <span className="quantity-control__label">Qtd.</span>
                        <button className="quantity-control__button" type="button" onClick={() => changeQuantity(item.productId, item.quantity - 1, item.availableStock)} disabled={busy || item.quantity <= 1} aria-label={`Diminuir quantidade de ${item.name}`}>−</button>
                        <span className="quantity-control__value" aria-live="polite">{busy ? "…" : item.quantity}</span>
                        <button className="quantity-control__button" type="button" onClick={() => changeQuantity(item.productId, item.quantity + 1, item.availableStock)} disabled={busy || item.quantity >= item.availableStock} aria-label={`Aumentar quantidade de ${item.name}`}>+</button>
                      </div>
                      <button className="remove-button" type="button" onClick={() => remove(item.productId)} disabled={busy}>Remover</button>
                    </div>
                  </div>
                  <strong className="cart-item__subtotal">{currency.format(item.subtotal)}</strong>
                </article>
              );
            })}
            <button className="clear-cart-button" type="button" onClick={clear} disabled={pendingId === "clear"}>{pendingId === "clear" ? "Esvaziando..." : "Esvaziar carrinho"}</button>
          </div>
          <aside className="cart-summary">
            <p className="eyebrow">Resumo</p><h2>Total da seleção</h2>
            <div className="cart-summary__line"><span>{cart.totalQuantity} {cart.totalQuantity === 1 ? "item" : "itens"}</span><strong>{currency.format(cart.subtotal)}</strong></div>
            <Link className="cart-button cart-button--link" href="/checkout">Continuar para checkout</Link>
            <p className="cart-note">Revise os itens e avance para informar os dados de entrega.</p>
          </aside>
        </div>
      )}
    </section>
  );
}
