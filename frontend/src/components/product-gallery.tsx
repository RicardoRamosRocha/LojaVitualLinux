"use client";

import { ChevronLeft, ChevronRight, RotateCcw, X, ZoomIn, ZoomOut } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { clampGalleryZoom, getGalleryIndex } from "@/lib/gallery-navigation";

type ProductGalleryProps = {
  images: string[];
  productName: string;
};

function imageAlt(productName: string, index: number) {
  return `${productName} — imagem ${index + 1}`;
}

export function ProductGallery({ images, productName }: ProductGalleryProps) {
  const [activeIndex, setActiveIndex] = useState(0);
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const [zoom, setZoom] = useState(1);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const lastFocusedRef = useRef<HTMLElement | null>(null);
  const touchStartX = useRef<number | null>(null);

  const hasImages = images.length > 0;
  const activeImage = images[activeIndex];

  function selectImage(index: number) {
    setActiveIndex(index);
    setZoom(1);
  }

  const moveImage = useCallback((direction: -1 | 1) => {
    setActiveIndex((current) => getGalleryIndex(current, images.length, direction));
    setZoom(1);
  }, [images.length]);

  function openLightbox() {
    if (!hasImages) return;
    lastFocusedRef.current = document.activeElement as HTMLElement | null;
    setLightboxOpen(true);
  }

  const closeLightbox = useCallback(() => {
    setLightboxOpen(false);
    setZoom(1);
    window.requestAnimationFrame(() => lastFocusedRef.current?.focus());
  }, []);

  useEffect(() => {
    if (!lightboxOpen) return;

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    closeButtonRef.current?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        closeLightbox();
      } else if (event.key === "ArrowLeft") {
        moveImage(-1);
      } else if (event.key === "ArrowRight") {
        moveImage(1);
      } else if (event.key === "Tab") {
        const dialog = event.currentTarget as HTMLElement;
        const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
          "button:not([disabled]), [href], input:not([disabled])",
        ));
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }
    }

    const dialog = document.querySelector<HTMLElement>("[data-product-lightbox]");
    dialog?.addEventListener("keydown", handleKeyDown);

    return () => {
      dialog?.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previousOverflow;
    };
  }, [closeLightbox, lightboxOpen, moveImage]);

  function handleTouchStart(event: React.TouchEvent) {
    touchStartX.current = event.touches[0]?.clientX ?? null;
  }

  function handleTouchEnd(event: React.TouchEvent) {
    if (touchStartX.current === null) return;
    const distance = (event.changedTouches[0]?.clientX ?? touchStartX.current) - touchStartX.current;
    touchStartX.current = null;
    if (Math.abs(distance) < 40) return;
    moveImage(distance > 0 ? -1 : 1);
  }

  if (!hasImages) {
    return <div className="product-gallery__placeholder">Angel</div>;
  }

  return (
    <div className="product-gallery">
      <button
        className="product-gallery__main"
        type="button"
        onClick={openLightbox}
        aria-label={`Ampliar imagem ${activeIndex + 1} de ${images.length}`}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          className="product-gallery__image"
          src={activeImage}
          alt={imageAlt(productName, activeIndex)}
          loading="eager"
          decoding="async"
        />
        <span className="product-gallery__expand-hint">Toque para ampliar</span>
      </button>

      {images.length > 1 && (
        <div className="product-gallery__thumbs" aria-label="Miniaturas das imagens do produto">
          {images.map((image, index) => (
            <button
              className={`product-gallery__thumb ${index === activeIndex ? "is-active" : ""}`}
              type="button"
              key={image}
              onClick={() => selectImage(index)}
              aria-label={`Mostrar imagem ${index + 1} de ${images.length}`}
              aria-current={index === activeIndex ? "true" : undefined}
            >
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={image} alt="" loading="lazy" decoding="async" />
            </button>
          ))}
        </div>
      )}

      {lightboxOpen && (
        <div
          className="product-lightbox"
          data-product-lightbox
          role="dialog"
          aria-modal="true"
          aria-label={`Galeria de ${productName}`}
          onTouchStart={handleTouchStart}
          onTouchEnd={handleTouchEnd}
        >
          <div className="product-lightbox__toolbar">
            <span>{activeIndex + 1} / {images.length}</span>
            <button
              ref={closeButtonRef}
              className="product-lightbox__button"
              type="button"
              onClick={closeLightbox}
              aria-label="Fechar galeria"
            >
              <X aria-hidden="true" size={21} />
            </button>
          </div>

          <button
            className="product-lightbox__arrow product-lightbox__arrow--previous"
            type="button"
            onClick={() => moveImage(-1)}
            aria-label="Imagem anterior"
          >
            <ChevronLeft aria-hidden="true" size={28} />
          </button>

          <div className="product-lightbox__stage">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              className="product-lightbox__image"
              src={activeImage}
              alt={imageAlt(productName, activeIndex)}
              style={{ transform: `scale(${zoom})` }}
              draggable="false"
            />
          </div>

          <button
            className="product-lightbox__arrow product-lightbox__arrow--next"
            type="button"
            onClick={() => moveImage(1)}
            aria-label="Próxima imagem"
          >
            <ChevronRight aria-hidden="true" size={28} />
          </button>

          <div className="product-lightbox__zoom" aria-label="Controles de zoom">
            <button className="product-lightbox__button" type="button" onClick={() => setZoom((value) => clampGalleryZoom(value - 0.25))} aria-label="Diminuir zoom">
              <ZoomOut aria-hidden="true" size={18} />
            </button>
            <button className="product-lightbox__button" type="button" onClick={() => setZoom(1)} aria-label="Restaurar zoom">
              <RotateCcw aria-hidden="true" size={17} />
            </button>
            <button className="product-lightbox__button" type="button" onClick={() => setZoom((value) => clampGalleryZoom(value + 0.25))} aria-label="Aumentar zoom">
              <ZoomIn aria-hidden="true" size={18} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
