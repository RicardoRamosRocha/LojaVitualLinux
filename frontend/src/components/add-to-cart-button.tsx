"use client";

import Link from "next/link";
import { useState } from "react";
import { addCartItem, fetchCart } from "@/lib/cart-client";

export function AddToCartButton({ productId, stock }: { productId: number; stock: number }) {
  const numericStock = Number(stock);
  const isAvailable = Number.isFinite(numericStock) && numericStock > 0;
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [added, setAdded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleAdd() {
    setPending(true);
    setAdded(false);
    setError(null);

    try {
      let token = csrfToken;
      if (!token) {
        const result = await fetchCart();
        token = result.csrfToken;
        setCsrfToken(token);
      }

      if (!token) throw new Error("Não foi possível proteger esta operação.");
      await addCartItem(productId, 1, token);
      setAdded(true);
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : "Não foi possível adicionar o produto.");
    } finally {
      setPending(false);
    }
  }

  if (!isAvailable) {
    return (
      <>
        <button className="cart-button" type="button" disabled>Adicionar ao carrinho</button>
        <p className="cart-note">Produto esgotado no momento.</p>
      </>
    );
  }

  return (
    <>
      <button className="cart-button cart-button--available" type="button" onClick={handleAdd} disabled={pending}>
        {pending ? "Adicionando..." : "Adicionar ao carrinho"}
      </button>
      {added && (
        <p className="cart-feedback" role="status">
          Produto adicionado. <Link href="/carrinho">Ver carrinho</Link> ou <Link href="/checkout">Ir para checkout</Link>.
        </p>
      )}
      {error && <p className="cart-error" role="alert">{error}</p>}
    </>
  );
}
