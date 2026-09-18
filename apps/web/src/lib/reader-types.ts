export type Book = {
  id: string;
  title: string;
  author: string;
  description: string | null;
  coverUrl: string | null;
  memoryPackAvailable: boolean;
};

export type SearchBook = {
  id: string | null;
  externalId: string | null;
  externalProvider: string | null;
  source: "local" | "external";
  title: string;
  author: string;
  coverUrl: string | null;
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
