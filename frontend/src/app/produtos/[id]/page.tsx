import type { Metadata } from "next";
import { Suspense } from "react";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getProduct, resolveMediaUrl } from "@/lib/api";
import type { Product } from "@/lib/types";
import { AddToCartButton } from "@/components/add-to-cart-button";

type ProductPageProps = {
  params: Promise<{ id: string }>;
};

const currency = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

function getImages(product: Product) {
  return Array.from(new Set([...(product.imageUrls ?? []), product.imageUrl].filter(Boolean)))
    .map((image) => resolveMediaUrl(image))
    .filter((image): image is string => Boolean(image));
}

function getVideos(product: Product) {
  return Array.from(new Set([...(product.videoUrls ?? []), product.videoUrl].filter(Boolean)))
    .map((video) => resolveMediaUrl(video))
    .filter((video): video is string => Boolean(video));
}

export async function generateMetadata({ params }: ProductPageProps): Promise<Metadata> {
  const { id } = await params;
  const result = await getProduct(id);

  if (!result.product) {
    return {
      title: result.notFound ? "Produto não encontrado | Angel Acessórios" : "Angel Acessórios",
      description: "Conheça a coleção Angel Acessórios.",
    };
  }

  return {
    title: `${result.product.name} | Angel Acessórios`,
    description: result.product.description ?? `Conheça ${result.product.name} na Angel Acessórios.`,
  };
}

function ProductDetailsFallback() {
  return (
    <main className="product-page product-page--message">
      <div className="product-page__inner">
        <div className="product-skeleton product-detail-skeleton" />
      </div>
    </main>
  );
}

async function ProductDetailsContent({ params }: ProductPageProps) {
  const { id } = await params;
  const result = await getProduct(id);

  if (result.notFound) notFound();

  if (result.unavailable || !result.product) {
    return (
      <main className="product-page product-page--message">
        <Link className="back-link" href="/#colecao">← Voltar à coleção</Link>
        <div className="catalog-message">
          <span className="catalog-message__eyebrow">Conexão indisponível</span>
          <h1>Não conseguimos carregar este produto.</h1>
          <p>Atualize a página em alguns instantes para tentar novamente.</p>
        </div>
      </main>
    );
  }

  const product = result.product;
  const images = getImages(product);
  const videos = getVideos(product);
  const isAvailable = product.stock > 0;
  const isLowStock = product.stock > 0 && product.stock <= 5;

  return (
    <main className="product-page">
      <div className="product-page__inner">
        <Link className="back-link" href="/#colecao">← Voltar à coleção</Link>

        <div className="product-detail">
          <div className="product-gallery">
            {images.length > 0 ? (
              images.map((image, index) => (
                // eslint-disable-next-line @next/next/no-img-element
                <img className="product-gallery__image" key={image} src={image} alt={`${product.name} — imagem ${index + 1}`} />
              ))
            ) : (
              <div className="product-gallery__placeholder">Angel</div>
            )}
          </div>

          <div className="product-detail__content">
            <p className="eyebrow">{product.category?.name ?? "Coleção Angel"}</p>
            <h1>{product.name}</h1>
            <p className="product-detail__price">{currency.format(product.price)}</p>
            <p className={`product-detail__availability ${!isAvailable ? "is-unavailable" : ""}`}>
              <span aria-hidden="true" />
              {!isAvailable ? "Esgotado" : isLowStock ? `Últimas ${product.stock} unidades` : `${product.stock} unidades disponíveis`}
            </p>
            {product.description && <p className="product-detail__description">{product.description}</p>}

            <AddToCartButton productId={product.id} stock={product.stock} />

            {videos.length > 0 && (
              <div className="product-videos">
                <p className="product-detail__label">Vídeos da peça</p>
                {videos.map((video) => (
                  <video className="product-video" controls preload="metadata" key={video}>
                    <source src={video} />
                    Seu navegador não suporta a reprodução de vídeo.
                  </video>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </main>
  );
}

export default function ProductDetailsPage(props: ProductPageProps) {
  return (
    <Suspense fallback={<ProductDetailsFallback />}>
      <ProductDetailsContent {...props} />
    </Suspense>
  );
}
