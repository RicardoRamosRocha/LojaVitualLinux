import { Suspense } from "react";
import Link from "next/link";
import { getProducts } from "@/lib/api";
import { groupProductsByCategory } from "@/lib/product-groups";
import type { Product } from "@/lib/types";
import { ProductCard } from "@/components/product-card";
import { HeroVideo } from "@/components/hero-video";

const backendUrl = (process.env.NEXT_PUBLIC_API_URL ?? "").replace(/\/$/, "");
const institutionalVideoUrl = backendUrl
  ? `${backendUrl}/videos/products/Video%20Home.mp4`
  : null;
const institutionalVideoPoster = backendUrl
  ? `${backendUrl}/images/products/banner-angel.jpg`
  : undefined;

function ProductGridFallback() {
  return (
    <div className="product-grid" aria-label="Carregando produtos" aria-busy="true">
      {Array.from({ length: 4 }, (_, index) => (
        <div className="product-skeleton" key={index} />
      ))}
    </div>
  );
}

function ProductCategorySection({ name, products }: { name: string; products: Product[] }) {
  const headingId = `category-${name.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;

  return (
    <section className="category-section" aria-labelledby={headingId}>
      <div className="category-section__heading">
        <h3 id={headingId}>{name}</h3>
      </div>
      <div className="category-carousel" role="region" aria-label={`Produtos de ${name}`}>
        {products.map((product) => <ProductCard key={product.id} product={product} />)}
      </div>
    </section>
  );
}

async function ProductSection() {
  const { products, unavailable } = await getProducts();

  if (unavailable) {
    return (
      <div className="catalog-message">
        <span className="catalog-message__eyebrow">Estamos preparando tudo</span>
        <h3>Não foi possível carregar a coleção agora.</h3>
        <p>Atualize a página em alguns instantes para ver as peças disponíveis.</p>
      </div>
    );
  }

  if (products.length === 0) {
    return (
      <div className="catalog-message">
        <span className="catalog-message__eyebrow">Em breve</span>
        <h3>Nossa coleção está sendo preparada.</h3>
        <p>Novas peças Angel Acessórios chegarão por aqui em breve.</p>
      </div>
    );
  }

  return (
    <div className="category-sections">
      {groupProductsByCategory(products).map((group) => (
        <ProductCategorySection key={group.key} name={group.name} products={group.products} />
      ))}
    </div>
  );
}

export default function Home() {
  return (
    <main>
      <section className="hero-shell">
        <nav className="site-nav" aria-label="Navegação principal">
          <a className="brand" href="#inicio" aria-label="Angel Acessórios, início">
            <span className="brand__mark">A</span>
            <span>
              <strong>Angel</strong>
              <small>Acessórios</small>
            </span>
          </a>
          <a className="nav-link" href="#colecao">Coleção</a>
          <Link className="nav-link" href="/pedidos">Meus pedidos</Link>
        </nav>

        <div className="hero-layout">
          <div className="hero-content" id="inicio">
            <p className="eyebrow">Angel Acessórios</p>
            <h1>Seu brilho, <em>seu momento.</em></h1>
            <a className="hero-cta" href="#colecao">Ver coleção <span>↘</span></a>
          </div>

          <div className="hero-video-shell" aria-label="Vídeo institucional Angel Acessórios">
            <div className="hero-video-fallback">
              <span>Angel</span>
              <small>Detalhes que iluminam</small>
            </div>
            {institutionalVideoUrl ? <HeroVideo src={institutionalVideoUrl} poster={institutionalVideoPoster} /> : null}
            <div className="hero-video-caption" aria-hidden="true">A delicadeza está nos detalhes</div>
          </div>
        </div>

        <div className="hero-orbit hero-orbit--one" aria-hidden="true" />
        <div className="hero-orbit hero-orbit--two" aria-hidden="true" />
        <span className="hero-spark hero-spark--one" aria-hidden="true">✦</span>
        <span className="hero-spark hero-spark--two" aria-hidden="true">✧</span>
      </section>

      <section className="collection-section" id="colecao">
        <div className="section-heading">
          <h2>A coleção Angel</h2>
        </div>
        <Suspense fallback={<ProductGridFallback />}>
          <ProductSection />
        </Suspense>
      </section>

      <footer className="site-footer">
        <span>Angel Acessórios</span>
        <span>Feito para iluminar você.</span>
      </footer>
    </main>
  );
}
