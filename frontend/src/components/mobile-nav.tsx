"use client";

import { House, LayoutGrid, Package, ShoppingBag } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { fetchCart } from "@/lib/cart-client";
import { getCartQuantity } from "@/lib/cart-summary";

const cartUpdatedEvent = "angel:cart-updated";

type MobileNavItem = {
  href: string;
  label: string;
  icon: typeof House;
};

const items: MobileNavItem[] = [
  { href: "/", label: "Início", icon: House },
  { href: "/#colecao", label: "Produtos", icon: LayoutGrid },
  { href: "/carrinho", label: "Carrinho", icon: ShoppingBag },
  { href: "/pedidos", label: "Pedidos", icon: Package },
];

function isActive(pathname: string, href: string, hash: string) {
  if (href === "/") return pathname === "/" && hash !== "#colecao";
  if (href === "/#colecao") return pathname === "/" && hash === "#colecao";
  if (href === "/pedidos") return pathname === "/pedidos" || pathname.startsWith("/pedidos/");
  return pathname === href;
}

export function MobileNav() {
  const pathname = usePathname();
  const [hash, setHash] = useState("");
  const [cartQuantity, setCartQuantity] = useState(0);

  const refreshCartQuantity = useCallback(() => {
    fetchCart()
      .then(({ cart }) => setCartQuantity(getCartQuantity(cart)))
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    const updateHash = () => setHash(window.location.hash);
    updateHash();
    window.addEventListener("hashchange", updateHash);
    return () => window.removeEventListener("hashchange", updateHash);
  }, []);

  useEffect(() => {
    refreshCartQuantity();
    window.addEventListener(cartUpdatedEvent, refreshCartQuantity);
    return () => window.removeEventListener(cartUpdatedEvent, refreshCartQuantity);
  }, [refreshCartQuantity]);

  return (
    <nav className="mobile-nav" aria-label="Navegação mobile">
      <div className="mobile-nav__inner">
        {items.map(({ href, label, icon: Icon }) => {
          const active = isActive(pathname, href, hash);
          const isCart = label === "Carrinho";
          return (
            <Link
              className={`mobile-nav__link${active ? " is-active" : ""}`}
              href={href}
              key={label}
              aria-current={active ? "page" : undefined}
            >
              <span className="mobile-nav__icon-wrap">
                <Icon aria-hidden="true" size={20} strokeWidth={active ? 2.2 : 1.8} />
                {isCart && cartQuantity > 0 && (
                  <span className="mobile-nav__badge" aria-label={`${cartQuantity} unidades no carrinho`}>
                    {cartQuantity > 99 ? "99+" : cartQuantity}
                  </span>
                )}
              </span>
              <span>{label}</span>
            </Link>
          );
        })}
      </div>
    </nav>
  );
}
