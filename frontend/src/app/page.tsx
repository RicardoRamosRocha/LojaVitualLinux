import { Suspense } from "react";
import Link from "next/link";
import { getProducts } from "@/lib/api";
import { ProductCard } from "@/components/product-card";

function ProductGridFallback() {
  return (
    <div className="product-grid" aria-label="Carregando produtos" aria-busy="true">
      {Array.from({ length: 4 }, (_, index) => (
        <div className="product-skeleton" key={index} />
      ))}
    </div>
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
    <div className="product-grid">
      {products.map((product) => (
        <ProductCard key={product.id} product={product} />
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

        <div className="hero-content" id="inicio">
          <p className="eyebrow">Detalhes que contam histórias</p>
          <h1>Seu brilho, <em>seu momento.</em></h1>
          <p className="hero-copy">
            Joias e acessórios escolhidos para acompanhar a sua essência com delicadeza,
            presença e um toque dourado.
          </p>
          <a className="hero-cta" href="#colecao">Conheça a coleção <span>↘</span></a>
        </div>

        <div className="hero-orbit hero-orbit--one" aria-hidden="true" />
        <div className="hero-orbit hero-orbit--two" aria-hidden="true" />
        <span className="hero-spark hero-spark--one" aria-hidden="true">✦</span>
        <span className="hero-spark hero-spark--two" aria-hidden="true">✧</span>
      </section>

      <section className="collection-section" id="colecao">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Para você escolher</p>
            <h2>A coleção Angel</h2>
          </div>
          <p className="section-description">Peças que transformam o cotidiano em ocasião especial.</p>
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
