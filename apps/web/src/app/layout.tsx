import type { Metadata, Viewport } from "next";
import { Geist } from "next/font/google";
import { PwaRegistration } from "@/components/pwa-registration";
import "./globals.css";

const geist = Geist({ variable: "--font-geist", subsets: ["latin"] });

export const metadata: Metadata = {
  title: { default: "Mnemora — Remember the story. Not the spoilers.", template: "%s | Mnemora" },
  description: "Lembre personagens, lugares e acontecimentos durante a leitura sem spoilers além do seu progresso.",
  applicationName: "Mnemora",
  category: "books",
  keywords: ["leitura", "livros", "personagens", "memória", "sem spoilers"],
  manifest: "/manifest.webmanifest",
  appleWebApp: {
    capable: true,
    statusBarStyle: "default",
    title: "Mnemora",
  },
  formatDetection: { telephone: false },
  icons: {
    icon: [
      { url: "/icons/icon-192.png", sizes: "192x192", type: "image/png" },
      { url: "/icons/icon-512.png", sizes: "512x512", type: "image/png" },
    ],
    apple: [{ url: "/icons/apple-touch-icon.png", sizes: "180x180", type: "image/png" }],
  },
  openGraph: {
    type: "website",
    locale: "pt_BR",
    siteName: "Mnemora",
    title: "Mnemora — Remember the story. Not the spoilers.",
    description: "Recupere personagens, lugares e acontecimentos exatamente até onde você leu.",
  },
  twitter: {
    card: "summary",
    title: "Mnemora",
    description: "Remember the story. Not the spoilers.",
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  colorScheme: "light dark",
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#f7f5ef" },
    { media: "(prefers-color-scheme: dark)", color: "#181b17" },
  ],
};

const themeScript = `
(() => {
  try {
    const saved = localStorage.getItem("mnemora-theme");
    if (saved === "light" || saved === "dark") {
      document.documentElement.dataset.theme = saved;
    }
  } catch {}
})();`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pt-BR" className={geist.variable} data-scroll-behavior="smooth" suppressHydrationWarning>
      <head><script dangerouslySetInnerHTML={{ __html: themeScript }} /></head>
      <body>
        <a className="skip-link" href="#main-content">Pular para o conteúdo</a>
        {children}
        <PwaRegistration />
      </body>
    </html>
  );
}
