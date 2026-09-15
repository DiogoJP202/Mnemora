import type { Metadata } from "next";
import { LibraryView } from "@/components/reader/library-view";
export const metadata: Metadata = { title: "Biblioteca" };
export default function LibraryPage() { return <LibraryView />; }
