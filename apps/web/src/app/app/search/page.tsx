import type { Metadata } from "next";
import { BookSearch } from "@/components/reader/book-search";
export const metadata: Metadata = { title: "Encontrar livros" };
export default function SearchPage() { return <BookSearch />; }
