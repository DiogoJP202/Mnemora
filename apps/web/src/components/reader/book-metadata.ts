import type { Book } from "@/lib/reader-types";

const languageNames = new Intl.DisplayNames(["pt-BR"], { type: "language" });

export type BookMetadataRow = {
  label: string;
  value: string;
};

export function detailMetadata(book: Book): BookMetadataRow[] {
  return [
    { label: "Editora", value: book.publisher },
    { label: "Publicação", value: formatPublishedDate(book.publishedDate) },
    { label: "Idioma", value: formatLanguage(book.language) },
    { label: "ISBN 13", value: book.isbn13 },
    { label: "ISBN 10", value: book.isbn10 },
  ].filter((row): row is BookMetadataRow => Boolean(row.value));
}

export function compactMetadata(book: Book): string[] {
  const values = [
    book.publisher,
    publicationYear(book.publishedDate),
    formatLanguage(book.language),
  ].filter((value): value is string => Boolean(value));

  if (values.length === 0) {
    const isbn = book.isbn13 ?? book.isbn10;
    if (isbn) values.push(`ISBN ${isbn}`);
  }

  return values.slice(0, 3);
}

function publicationYear(value: string | null): string | null {
  const match = value?.match(/^(\d{4})/);
  return match?.[1] ?? null;
}

function formatPublishedDate(value: string | null): string | null {
  if (!value) return null;
  return publicationYear(value) ?? value;
}

function formatLanguage(value: string | null): string | null {
  if (!value) return null;

  try {
    const label = languageNames.of(value.toLowerCase());
    return label ? `${label.charAt(0).toUpperCase()}${label.slice(1)}` : value.toUpperCase();
  } catch {
    return value.toUpperCase();
  }
}
