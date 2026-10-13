export type AdminBook = {
  id: string; title: string; author: string;
  description: string | null; coverUrl: string | null;
  catalogKind: "Curated" | "Imported";
  externalProvider: string | null; externalId: string | null;
  isbn10: string | null; isbn13: string | null;
  publisher: string | null; publishedDate: string | null; language: string | null;
  readingOutlineSourceUrl: string | null; readingOutlineAttribution: string | null;
  readingOutlineStatus: "None" | "Draft" | "Published";
  loreStatus: "None" | "Draft" | "Published";
  readingOutlinePublishedAt: string | null; lorePublishedAt: string | null;
  requestCount: number; unitCount: number; entityCount: number;
  factCount: number; relationCount: number; eventCount: number;
};
export type AdminUnit = {
  id: string; bookId: string; parentUnitId: string | null;
  title: string; safeLabel: string; slug: string;
  type: string; orderIndex: number;
};
export type AdminEntity = {
  id: string; bookId: string; type: string; name: string; slug: string;
  shortDescription: string | null; imageUrl: string | null;
  firstKnownAtUnitId: string; importance: number;
  chronologyIndex: number | null;
};
export type AdminAlias = { id: string; alias: string; revealAtUnitId: string };
export type AdminFact = {
  id: string; content: string; memoryHint: string | null;
  type: string; importance: number; revealAtUnitId: string;
};
export type AdminRelation = {
  id: string; bookId: string; sourceEntityId: string; targetEntityId: string;
  relationType: string; label: string | null; revealAtUnitId: string;
};
export type AdminEntityDetail = {
  entity: AdminEntity; aliases: AdminAlias[];
  facts: AdminFact[]; relations: AdminRelation[];
};

export type MemoryPackCounts = {
  readingUnits: number; entities: number; aliases: number;
  facts: number; relations: number; events: number;
};
export type MemoryPackValidation = {
  isValid: boolean; alreadyImported: boolean;
  counts: MemoryPackCounts;
  issues: { path: string; message: string }[];
};
export type MemoryPackImportResult = {
  alreadyImported: boolean; counts: MemoryPackCounts;
};
