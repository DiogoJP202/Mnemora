"use client";

import { useRef, useState } from "react";
import { apiGet, apiWriteJsonDocument, ApiResponseError, downloadJson } from "@/lib/api";
import type { AdminBook, GeneratedMemoryPackSelection, MemoryPackImportResult, MemoryPackValidation } from "@/lib/admin-types";

const MAX_FILE_BYTES = 2 * 1024 * 1024;
const countLabels = {
  readingUnits: "Unidades", entities: "Entidades", aliases: "Aliases",
  facts: "Fatos", relations: "Relações", events: "Eventos",
} as const;

type Selection = {
  fileName: string; json: string; book: Record<string, unknown>;
  originalJson?: string; isPartial: boolean; reportHash?: string;
};
type Props = {
  book: AdminBook;
  disabled: boolean;
  disabledReason?: string;
  onWorkingChange: (working: boolean) => void;
  onImported: () => Promise<void>;
  generatedSelection?: GeneratedMemoryPackSelection | null;
};

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown> : {};
}

function display(value: unknown): string {
  return typeof value === "string" && value.trim()
    ? value.length > 250 ? `${value.slice(0, 250)}…` : value
    : "Não informado";
}

function bookFromJson(json: string): Record<string, unknown> {
  try { return record(record(JSON.parse(json)).book); }
  catch { return {}; }
}

function errorValidation(cause: unknown): MemoryPackValidation | null {
  if (!(cause instanceof ApiResponseError)) return null;
  const payload = record(cause.payload);
  const counts = record(payload.counts);
  if (payload.isValid !== false || typeof payload.alreadyImported !== "boolean"
    || !Object.keys(countLabels).every((key) => typeof counts[key] === "number")
    || !Array.isArray(payload.issues)
    || !payload.issues.every((issue: unknown) => {
      const value = record(issue);
      return typeof value.path === "string" && typeof value.message === "string";
    })) return null;
  return payload as MemoryPackValidation;
}

export function AdminMemoryPackTransfer({ book, disabled, disabledReason, onWorkingChange, onImported, generatedSelection }: Props) {
  const [selection, setSelection] = useState<Selection | null>(() => generatedSelection ? {
    fileName: `mnemora-${book.id}-gerado-v1.json`, json: generatedSelection.json,
    originalJson: generatedSelection.originalJson, isPartial: generatedSelection.isPartial,
    reportHash: generatedSelection.reportHash, book: bookFromJson(generatedSelection.json),
  } : null);
  const [validation, setValidation] = useState<MemoryPackValidation | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [reviewed, setReviewed] = useState(generatedSelection?.reviewed ?? false);
  const [partialAcknowledged, setPartialAcknowledged] = useState(generatedSelection?.partialAcknowledged ?? false);
  const [working, setWorking] = useState<"read" | "validate" | "import" | "export" | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const reportHeading = useRef<HTMLHeadingElement>(null);
  const isBusy = disabled || working !== null;
  const hasContent = book.unitCount > 0 || book.entityCount > 0;
  const isGenerated = selection?.originalJson !== undefined;
  const reviewReady = !isGenerated || reviewed && (!selection?.isPartial || partialAcknowledged);
  const edited = isGenerated && selection?.json !== selection?.originalJson;

  function start(action: NonNullable<typeof working>) {
    setWorking(action);
    onWorkingChange(true);
    setError("");
    setSuccess("");
  }

  function finish() {
    setWorking(null);
    onWorkingChange(false);
  }

  async function selectFile(file: File | undefined) {
    setSelection(null);
    setValidation(null);
    setConfirmed(false);
    setReviewed(false);
    setPartialAcknowledged(false);
    setError("");
    setSuccess("");
    if (!file) return;
    if (file.size > MAX_FILE_BYTES) {
      setError("O arquivo ultrapassa o limite de 2 MiB. Reduza o tamanho do pacote antes de importar.");
      return;
    }
    start("read");
    try {
      const json = await file.text();
      const payload: unknown = JSON.parse(json);
      setSelection({ fileName: file.name, json, book: record(record(payload).book), isPartial: false });
    } catch {
      setError("Não foi possível ler o JSON. Confira a sintaxe e selecione o arquivo corrigido.");
    } finally {
      finish();
    }
  }

  async function validate() {
    if (!selection || !reviewReady) return;
    if (new TextEncoder().encode(selection.json).byteLength > MAX_FILE_BYTES) {
      setError("O JSON ultrapassa o limite de 2 MiB. Revise o tamanho antes de validar.");
      return;
    }
    start("validate");
    setValidation(null);
    setConfirmed(false);
    try {
      const result = await apiWriteJsonDocument<MemoryPackValidation>(
        `/api/admin/books/${book.id}/memory-pack/validate`, selection.json,
      );
      setValidation(result);
      requestAnimationFrame(() => reportHeading.current?.focus());
    } catch (cause) {
      const result = errorValidation(cause);
      if (result) {
        setValidation(result);
        requestAnimationFrame(() => reportHeading.current?.focus());
      } else setError(cause instanceof Error ? cause.message : "Não foi possível validar o pacote.");
    } finally {
      finish();
    }
  }

  async function importPack() {
    if (!selection || !validation?.isValid || !confirmed || !reviewReady) return;
    start("import");
    try {
      const result = await apiWriteJsonDocument<MemoryPackImportResult>(
        `/api/admin/books/${book.id}/memory-pack/import`, selection.json,
      );
      setValidation({ isValid: true, alreadyImported: true, counts: result.counts, issues: [] });
      setConfirmed(false);
      setSuccess(result.alreadyImported
        ? "Este pacote já corresponde ao conteúdo do livro. Nenhum dado foi alterado."
        : "Pacote importado como rascunho. Confira o preview e publique o índice e o conhecimento quando estiverem revisados.");
      await onImported();
    } catch (cause) {
      const result = errorValidation(cause);
      setValidation(result);
      setConfirmed(false);
      if (!result) setError(cause instanceof Error ? cause.message : "Não foi possível importar o pacote.");
    } finally {
      finish();
    }
  }

  async function exportPack() {
    start("export");
    try {
      const document = await apiGet<unknown>(`/api/admin/books/${book.id}/memory-pack/export`);
      downloadJson(`mnemora-${book.id}-pack-v1.json`, JSON.stringify(document, null, 2));
      setSuccess(hasContent
        ? "Exportação preparada. O arquivo contém o conteúdo editorial completo, inclusive revelações futuras."
        : "Modelo preparado com a identidade desta edição. Preencha as unidades e o conhecimento antes de validar.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível exportar o pacote.");
    } finally {
      finish();
    }
  }

  function editJson(json: string) {
    if (!selection) return;
    setSelection({ ...selection, json, book: bookFromJson(json) });
    setValidation(null);
    setConfirmed(false);
    setReviewed(false);
    setPartialAcknowledged(false);
    setError("");
    setSuccess("");
  }

  return (
    <section className="admin-pack-transfer mt-8 max-w-4xl rounded-2xl border bg-[var(--surface)] p-5" aria-labelledby="pack-transfer-title">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <span className="section-index">PACOTE COMPLETO</span>
          <h2 id="pack-transfer-title" className="mt-2 text-2xl font-semibold">Importar capítulos e conhecimento</h2>
          <p className="mt-2 max-w-2xl text-sm text-[var(--ink-soft)]">
            Use um arquivo JSON v1 para reunir unidades, entidades, aliases, fatos e relações. A validação não grava dados; a importação cria um rascunho para revisão.
          </p>
          <p className="mt-2 text-sm">Destino: <strong>{book.title}</strong> · {book.author}</p>
        </div>
        <button type="button" disabled={isBusy} onClick={() => void exportPack()} className="rounded-lg border px-4 py-2 text-sm disabled:opacity-50">
          {working === "export" ? "Preparando arquivo…" : hasContent ? "Exportar pacote" : "Baixar modelo desta edição"}
        </button>
      </div>

      {hasContent && (
        <p className="mt-4 text-sm text-[var(--ink-soft)]">
          Este livro já tem conteúdo. A importação aceita uma cópia idêntica e preserva seus vínculos. Alterações continuam pelos editores de unidades e conhecimento.
        </p>
      )}
      {disabledReason && <p role="status" className="mt-4 text-sm">{disabledReason}</p>}

      <div className="mt-5 grid gap-3">
        <label htmlFor="pack-file" className="text-sm font-semibold">Arquivo do pacote</label>
        <input id="pack-file" type="file" accept=".json,application/json" disabled={isBusy} onChange={(event) => void selectFile(event.target.files?.[0])} aria-describedby="pack-file-help" className="w-full min-w-0 max-w-full rounded-lg border p-3 text-sm" />
        <p id="pack-file-help" className="text-xs text-[var(--ink-soft)]">
          Até 2 MiB. Baixe o modelo do livro para conservar sua identidade e confira a sequência da edição. O arquivo não pode conter notas ou progresso de leitores.
        </p>
      </div>

      {selection && (
        <div className="admin-pack-file mt-5 rounded-xl border p-4">
          <h3 className="break-words text-sm font-semibold">{selection.fileName}</h3>
          {isGenerated && <p className="mt-2 break-all text-xs text-[var(--ink-soft)]">Relatório original vinculado ao hash {selection.reportHash}.</p>}
          {selection.isPartial && <p className="mt-3 rounded-lg border border-[var(--line)] bg-[var(--surface-tint)] p-3 text-sm">Resultado parcial. Confira os capítulos sem fontes, falhas e evidências antes de importar.</p>}
          {edited && <p role="status" className="mt-3 rounded-lg border border-[var(--line)] p-3 text-sm">O JSON foi editado. O relatório e seu hash continuam ligados à versão originalmente gerada; as evidências podem não corresponder às alterações. Revise novamente e valide este texto.</p>}
          <dl className="admin-edition-grid mt-3">
            <div><dt>Livro do arquivo</dt><dd>{display(selection.book.title)}</dd></div>
            <div><dt>Autor</dt><dd>{display(selection.book.author)}</dd></div>
            <div><dt>ISBN</dt><dd>{display(selection.book.isbn13 ?? selection.book.isbn10)}</dd></div>
            <div><dt>Provedor</dt><dd>{display(selection.book.externalProvider)}</dd></div>
            <div><dt>ID externo</dt><dd>{display(selection.book.externalId)}</dd></div>
            <div><dt>Idioma</dt><dd>{display(selection.book.language)}</dd></div>
          </dl>
          <details className="mt-4 min-w-0">
            <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Editar JSON (opcional)</summary>
            <label htmlFor="pack-json" className="mt-2 block text-sm">JSON para validação e importação</label>
            <textarea id="pack-json" rows={12} spellCheck={false} disabled={isBusy} value={selection.json} onChange={(event) => editJson(event.target.value)} aria-describedby="pack-json-help" className="mt-2 w-full min-w-0 max-w-full rounded-lg border p-3 font-mono text-xs" />
            <p id="pack-json-help" className="mt-2 text-xs text-[var(--ink-soft)]">Qualquer edição invalida a validação e as confirmações anteriores. O relatório original permanece disponível na geração acima.</p>
          </details>
          {isGenerated && <div className="mt-4 grid gap-3">
            <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={isBusy} checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} className="mt-1 shrink-0" /><span>Revisei este JSON, suas evidências e os pontos de revelação, incluindo as alterações feitas.</span></label>
            {selection.isPartial && <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={isBusy} checked={partialAcknowledged} onChange={(event) => setPartialAcknowledged(event.target.checked)} className="mt-1 shrink-0" /><span>Reconheço que o pacote é parcial e conferi suas lacunas antes de validar e importar.</span></label>}
          </div>}
          <button type="button" disabled={isBusy || !reviewReady} onClick={() => void validate()} className="mt-4 min-h-11 rounded-lg border px-4 py-2 disabled:opacity-50">
            {working === "validate" ? "Validando…" : "Validar arquivo"}
          </button>
          <button type="button" disabled={isBusy} onClick={() => downloadJson(selection.fileName, selection.json)} className="mt-4 ml-2 min-h-11 rounded-lg border px-4 py-2 disabled:opacity-50">{edited ? "Baixar JSON revisado" : "Baixar JSON selecionado"}</button>
        </div>
      )}

      {validation && (
        <div className="mt-5" aria-live="polite">
          <h3 ref={reportHeading} tabIndex={-1} className="text-lg font-semibold">
            {validation.isValid
              ? validation.alreadyImported ? "Pacote já presente neste livro" : "Estrutura validada para importação"
              : "Corrija o arquivo antes de importar"}
          </h3>
          <dl className="admin-transfer-counts mt-3">
            {Object.entries(countLabels).map(([key, label]) => (
              <div key={key}><dt>{label}</dt><dd>{validation.counts[key as keyof typeof countLabels]}</dd></div>
            ))}
          </dl>
          {validation.issues.length > 0 && (
            <ul className="admin-pack-issues mt-4 grid gap-2 text-sm">
              {validation.issues.slice(0, 20).map((issue, index) => (
                <li key={`${issue.path}-${index}`}><strong>{issue.path}</strong>: {issue.message}</li>
              ))}
              {validation.issues.length > 20 && <li>Mais {validation.issues.length - 20} problemas encontrados. Corrija o arquivo e valide novamente.</li>}
            </ul>
          )}
          {validation.isValid && (
            <div className="mt-5 grid gap-4">
              <label className="flex min-h-11 items-start gap-3 text-sm">
                <input type="checkbox" checked={confirmed} disabled={isBusy} onChange={(event) => setConfirmed(event.target.checked)} className="mt-1 shrink-0" />
                <span>Conferi que o pacote pertence a <strong>{book.title}</strong>, de {book.author}, e que os capítulos correspondem a esta edição. A revisão do texto e dos pontos de revelação continuará no preview.</span>
              </label>
              <button type="button" disabled={isBusy || !confirmed || !reviewReady} onClick={() => void importPack()} className="min-h-11 justify-self-start rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)] disabled:opacity-50">
                {working === "import" ? "Importando…" : validation.alreadyImported ? "Confirmar pacote existente" : "Importar como rascunho"}
              </button>
            </div>
          )}
        </div>
      )}

      {error && <p role="alert" className="form-alert mt-4 rounded-lg border p-3 text-sm">{error}</p>}
      {success && <p role="status" className="mt-4 text-sm">{success}</p>}
    </section>
  );
}
