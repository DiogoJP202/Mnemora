export type LoreEntitySummary = {
  id: string;
  bookId: string;
  type: string;
  name: string;
  shortDescription: string | null;
  imageUrl: string | null;
  memoryHint: string | null;
};

export type LoreFact = {
  id: string;
  content: string;
  memoryHint: string | null;
  type: string;
};

export type LoreRelation = {
  id: string;
  sourceId: string;
  sourceName: string;
  targetId: string;
  targetName: string;
  relationType: string;
  label: string | null;
};

export type LoreEntityDetail = {
  entity: LoreEntitySummary;
  aliases: string[];
  facts: LoreFact[];
  relations: LoreRelation[];
};

export type TimelineEvent = {
  entity: LoreEntitySummary;
  chronologyIndex: number | null;
};
