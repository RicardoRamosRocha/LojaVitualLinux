import type { Metadata } from "next";
import { Suspense } from "react";
import { MobileNav } from "@/components/mobile-nav";
import "./globals.css";

export const metadata: Metadata = {
  title: "Angel Acessórios | Detalhes que contam histórias",
  description: "Joias e acessórios escolhidos para iluminar seus momentos.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="pt-BR">
      <body>{children}<Suspense fallback={null}><MobileNav /></Suspense></body>
    </html>
  );
}
