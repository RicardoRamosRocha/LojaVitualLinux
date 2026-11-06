import { resolveMediaUrl } from "@/lib/api";
import type { Product } from "@/lib/types";
import Link from "next/link";

const currency = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

function getProductImage(product: Product) {
  return resolveMediaUrl(product.imageUrls[0] ?? product.imageUrl);
}

export function ProductCard({ product, showCategory = false }: { product: Product; showCategory?: boolean }) {
  const image = getProductImage(product);
  const isAvailable = product.stock > 0;

  return (
    <Link className="product-card" href={`/produtos/${product.id}`}>
      <article>
        <div className="product-card__image-wrap">
          {image ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img className="product-card__image" src={image} alt={product.name} />
          ) : (
            <div className="product-card__image-placeholder" aria-hidden="true">
              <span>Angel</span>
            </div>
          )}
        </div>

        <div className="product-card__content">
          {showCategory && <p className="product-card__category">{product.category?.name ?? "Acessórios"}</p>}
          <h3>{product.name}</h3>
          <p className="product-card__price">{currency.format(product.price)}</p>
          <span className={`product-card__action ${!isAvailable ? "is-unavailable" : ""}`}>{isAvailable ? "Comprar" : "Esgotado"}<span aria-hidden="true">↗</span></span>
        </div>
      </article>
    </Link>
  );
}
