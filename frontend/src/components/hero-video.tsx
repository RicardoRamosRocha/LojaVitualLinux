"use client";

import { Volume2, VolumeX } from "lucide-react";
import { useRef, useState } from "react";

type HeroVideoProps = {
  src: string;
  poster?: string;
};

export function HeroVideo({ src, poster }: HeroVideoProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [muted, setMuted] = useState(true);
  const [failed, setFailed] = useState(false);

  function toggleSound() {
    const nextMuted = !muted;
    if (videoRef.current) {
      videoRef.current.muted = nextMuted;
      if (!nextMuted) {
        void videoRef.current.play().catch(() => undefined);
      }
    }
    setMuted(nextMuted);
  }

  return (
    <>
      <video
        ref={videoRef}
        className={`hero-video${failed ? " is-hidden" : ""}`}
        autoPlay
        muted={muted}
        loop
        playsInline
        preload="metadata"
        poster={poster}
        onError={() => setFailed(true)}
      >
        <source src={src} type="video/mp4" />
        Seu navegador não suporta vídeo HTML5.
      </video>
      {!failed && <button
        className="hero-video-control"
        type="button"
        onClick={toggleSound}
        aria-label={muted ? "Ativar som do vídeo" : "Desativar som do vídeo"}
        aria-pressed={!muted}
      >
        {muted ? <VolumeX aria-hidden="true" size={17} /> : <Volume2 aria-hidden="true" size={17} />}
        <span>{muted ? "Som desligado" : "Som ligado"}</span>
      </button>}
    </>
  );
}
