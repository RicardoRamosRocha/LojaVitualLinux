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

export function ProductCard({ product }: { product: Product }) {
  const image = getProductImage(product);
  const isAvailable = product.stock > 0;
  const isLowStock = product.stock > 0 && product.stock <= 5;

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
          {product.isFeatured && <span className="product-card__badge">Destaque</span>}
        </div>

        <div className="product-card__content">
          <p className="product-card__category">{product.category?.name ?? "Acessórios"}</p>
          <h3>{product.name}</h3>
          <p className="product-card__price">{currency.format(product.price)}</p>
          <p className={`product-card__stock ${!isAvailable ? "is-unavailable" : ""}`}>
            <span aria-hidden="true" />
            {!isAvailable ? "Esgotado" : isLowStock ? `Últimas ${product.stock} unidades` : "Disponível"}
          </p>
        </div>
      </article>
    </Link>
  );
}
