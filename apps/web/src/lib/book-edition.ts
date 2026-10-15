import type { SearchBook } from "@/lib/reader-types";

const languageNames: Record<string, string> = {
  de: "Alemão", deu: "Alemão", eng: "Inglês", en: "Inglês", es: "Espanhol", spa: "Espanhol",
  fr: "Francês", fra: "Francês", it: "Italiano", ita: "Italiano", por: "Português", pt: "Português", "pt-br": "Português (Brasil)",
};

export function publicationYear(publishedDate: string | null) {
  return publishedDate?.match(/^\d{4}/)?.[0] ?? null;
}

export function languageName(language: string | null) {
  return language ? languageNames[language.trim().toLowerCase()] ?? language : null;
}

export function preferredIsbn(book: SearchBook) {
  return book.isbn13 ?? book.isbn10;
}

export function metadataRows(book: SearchBook) {
  return [
    { label: "Título", value: book.title },
    { label: "Subtítulo", value: book.subtitle },
    { label: "Autoria", value: book.author },
    { label: "Editora", value: book.publisher },
    { label: "Publicação", value: publicationYear(book.publishedDate) },
    { label: "Idioma", value: languageName(book.language) },
    { label: "ISBN-13", value: book.isbn13 },
    { label: "ISBN-10", value: book.isbn10 },
    { label: "Edição", value: book.edition },
    { label: "Extensão", value: book.pageCount ? `${book.pageCount} páginas` : null },
    { label: "Categorias", value: book.categories?.length ? book.categories.join(", ") : null },
  ].filter((row): row is { label: string; value: string } => Boolean(row.value));
}
