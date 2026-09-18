export type Book = {
  id: string;
  title: string;
  subtitle: string | null;
  author: string;
  description: string | null;
  coverUrl: string | null;
  isbn10: string | null;
  isbn13: string | null;
  publisher: string | null;
  publishedDate: string | null;
  language: string | null;
  memoryPackAvailable: boolean;
};

export type SearchBook = {
  id: string | null;
  externalId: string | null;
  externalProvider: string | null;
  source: "local" | "external";
  title: string;
  subtitle: string | null;
  author: string;
  coverUrl: string | null;
  publisher: string | null;
  publishedDate: string | null;
  language: string | null;
  isbn10: string | null;
  isbn13: string | null;
  pageCount: number | null;
  categories: string[] | null;
  edition: string | null;
  memoryPackAvailable: boolean;
};

export type LibraryBook = {
  book: Book;
  status: "WantToRead" | "Reading" | "Paused" | "Finished";
  currentReadingUnitId: string | null;
  currentPage: number | null;
  progressPercentage: number | null;
};

export type ReadingUnit = {
  id: string;
  parentUnitId: string | null;
  label: string;
  title: string;
  type: string;
  orderIndex: number;
  isKnown: boolean;
  isCurrent: boolean;
};
