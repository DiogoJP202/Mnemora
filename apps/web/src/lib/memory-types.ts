export type UserNote = {
  id: string;
  bookId: string;
  entityId: string | null;
  readingUnitId: string | null;
  content: string;
  createdAt: string;
  updatedAt: string;
};
