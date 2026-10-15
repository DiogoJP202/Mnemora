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

export type GenerationSource = { title: string; summary: string };
export type GenerationSourceIssue = { path: string; code: string; message: string; severity: string; isBlocking: boolean };
export type GenerationSourceParseResult = { sources: GenerationSource[]; issues: GenerationSourceIssue[]; isValid: boolean };
export type GenerationCoverage = { unitSlug: string; title: string; status: "Pending" | "Generated" | "MissingSource" | "Failed"; sourceHash: string };
export type GenerationSession = {
  runId: string; bookId: string; revision: number;
  state: "Ready" | "Processing" | "Paused" | "Completed" | "Cancelled";
  completedUnits: number; totalUnits: number; calls: number; tokens: number; estimatedTokens: number;
  nextAllowedAt: string | null; coverage: GenerationCoverage[]; diagnostics: string[]; hasImportableResult: boolean;
};
export type GenerationAvailability = {
  enabled: boolean; reason: string | null; model: string; maxUnits: number; maxSourceBytes: number;
  minCallIntervalSeconds: number; existingSession: GenerationSession | null;
};
export type GenerationEvidence = {
  unitSlug: string; sourceHash: string; paragraphId: string; quote: string; justification: string; itemId: string; jsonPath: string;
};
export type GenerationReport = {
  provider: string; model: string; promptVersion: string; sourceAttribution: string | null; sourceUrl: string | null;
  indexComplete: boolean; partial: boolean; packHash: string; calls: number; tokens: number; estimatedTokens: number;
  units: GenerationCoverage[]; evidence: GenerationEvidence[]; diagnostics: string[];
};
export type GenerationResult = {
  status: GenerationSession; document: Record<string, unknown>; documentJson: string;
  report: GenerationReport; validation: MemoryPackValidation;
};
export type GeneratedMemoryPackSelection = {
  id: string; json: string; originalJson: string; isPartial: boolean; reportHash: string;
  reviewed: boolean; partialAcknowledged: boolean;
};
