import type { Metadata } from "next";
import { Geist } from "next/font/google";
import "./globals.css";

const geist = Geist({ variable: "--font-geist", subsets: ["latin"] });

export const metadata: Metadata = {
  title: { default: "Mnemora — Remember the story. Not the spoilers.", template: "%s | Mnemora" },
  description: "Lembre personagens, lugares e acontecimentos durante a leitura sem spoilers além do seu progresso.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return <html lang="pt-BR" className={geist.variable}><body>{children}</body></html>;
}
