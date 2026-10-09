import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

export const DEVELOPMENT_EXCEPTION = Object.freeze({
  advisory: "GHSA-vfj7-8cjw-p6xm",
  url: "https://github.com/advisories/GHSA-vfj7-8cjw-p6xm",
  expires: "2026-11-09",
});

const chain = Object.freeze({
  "eslint-config-next": { version: "16.3.8", via: "@next/eslint-plugin-next", effects: [], range: ">=14.3.0-canary.0", direct: true },
  "@next/eslint-plugin-next": { version: "16.3.8", via: "fast-glob", effects: ["eslint-config-next"], range: ">=14.3.0-canary.0", direct: false },
  "fast-glob": { version: "3.3.1", via: "micromatch", effects: ["@next/eslint-plugin-next"], range: "*", direct: false },
  micromatch: { version: "4.0.8", via: "braces", effects: ["fast-glob"], range: ">=0.2.0", direct: false },
  braces: { version: "3.0.3", via: null, effects: ["micromatch"], range: "*", direct: false },
});
const severities = ["info", "low", "moderate", "high", "critical"];
const rootDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function requireCondition(condition, message) {
  if (!condition) throw new Error(message);
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function requireKeys(value, allowed, label) {
  requireCondition(isObject(value), `${label}: objeto esperado.`);
  requireCondition(Object.keys(value).every((key) => allowed.includes(key)),
    `${label}: estrutura desconhecida.`);
}

function sameStrings(actual, expected) {
  return Array.isArray(actual) && actual.length === expected.length
    && actual.every((value) => typeof value === "string")
    && [...actual].sort().every((value, index) => value === [...expected].sort()[index]);
}

function validateAdvisory(advisory, label) {
  requireKeys(advisory,
    ["source", "name", "dependency", "title", "url", "severity", "cwe", "cvss", "range"], label);
  requireCondition(Number.isInteger(advisory.source) && advisory.source > 0
    && ["name", "dependency", "title", "url", "range"].every((key) =>
      typeof advisory[key] === "string" && advisory[key].length > 0)
    && severities.includes(advisory.severity)
    && Array.isArray(advisory.cwe) && advisory.cwe.every((value) => typeof value === "string"),
  `${label}: advisory desconhecido ou inválido.`);
  requireKeys(advisory.cvss, ["score", "vectorString"], `${label}.cvss`);
  requireCondition((advisory.cvss.score === null || (Number.isFinite(advisory.cvss.score)
      && advisory.cvss.score >= 0 && advisory.cvss.score <= 10))
    && (advisory.cvss.vectorString === null || typeof advisory.cvss.vectorString === "string"),
  `${label}: CVSS inválido.`);
}

function validateReport(report, exitCode) {
  requireKeys(report, ["auditReportVersion", "vulnerabilities", "metadata"], "npm audit");
  requireCondition(report.auditReportVersion === 2 && isObject(report.vulnerabilities),
    "Formato desconhecido do relatório npm audit; esperado auditReportVersion 2.");
  requireKeys(report.metadata, ["vulnerabilities", "dependencies"], "npm audit.metadata");
  requireKeys(report.metadata.vulnerabilities, [...severities, "total"], "Contagens npm audit");
  requireCondition(isObject(report.metadata.dependencies), "Metadados de dependências ausentes.");
  const counts = Object.fromEntries(severities.map((severity) => [severity, 0]));
  for (const [name, vulnerability] of Object.entries(report.vulnerabilities)) {
    requireKeys(vulnerability,
      ["name", "severity", "isDirect", "via", "effects", "range", "nodes", "fixAvailable"], name);
    requireCondition(vulnerability.name === name && severities.includes(vulnerability.severity)
      && typeof vulnerability.isDirect === "boolean" && typeof vulnerability.range === "string"
      && Array.isArray(vulnerability.via) && vulnerability.via.length > 0
      && Array.isArray(vulnerability.effects) && vulnerability.effects.every((value) => typeof value === "string")
      && Array.isArray(vulnerability.nodes) && vulnerability.nodes.length > 0
      && vulnerability.nodes.every((value) => typeof value === "string" && value.startsWith("node_modules/")),
    `${name}: estrutura de vulnerabilidade inválida.`);
    const fix = vulnerability.fixAvailable;
    if (typeof fix !== "boolean") {
      requireKeys(fix, ["name", "version", "isSemVerMajor"], `${name}.fixAvailable`);
      requireCondition(typeof fix.name === "string" && typeof fix.version === "string"
        && typeof fix.isSemVerMajor === "boolean", `${name}: correção desconhecida.`);
    }
    for (const via of vulnerability.via) {
      if (typeof via === "string") {
        requireCondition(Object.hasOwn(report.vulnerabilities, via),
          `${name}: referência de advisory desconhecida (${via}).`);
      } else validateAdvisory(via, `${name}.via`);
    }
    counts[vulnerability.severity] += 1;
  }
  for (const severity of severities) {
    requireCondition(Number.isInteger(report.metadata.vulnerabilities[severity])
      && report.metadata.vulnerabilities[severity] === counts[severity],
    `Contagem inconsistente de ${severity} no npm audit.`);
  }
  const total = Object.values(counts).reduce((sum, value) => sum + value, 0);
  requireCondition(report.metadata.vulnerabilities.total === total,
    "Contagem total inconsistente no npm audit.");
  requireCondition(exitCode === (counts.high + counts.critical > 0 ? 1 : 0),
    "npm audit encerrou com código inesperado ou incompatível com o relatório.");
  return counts;
}

function validateDevelopmentChain(report, lock, now) {
  requireCondition(now instanceof Date && Number.isFinite(now.getTime()), "Data de auditoria inválida.");
  requireCondition(now.getTime() < Date.parse(`${DEVELOPMENT_EXCEPTION.expires}T00:00:00.000Z`),
    `A exceção de desenvolvimento ${DEVELOPMENT_EXCEPTION.advisory} venceu em ${DEVELOPMENT_EXCEPTION.expires} UTC.`);
  requireCondition(isObject(lock) && lock.lockfileVersion === 3 && isObject(lock.packages)
    && isObject(lock.packages[""]), "Lockfile desconhecido; esperado package-lock v3.");
  const root = lock.packages[""];
  requireCondition(root.devDependencies?.["eslint-config-next"] === "16.3.8",
    "eslint-config-next precisa ser uma dependência de desenvolvimento exata.");
  for (const [name, expected] of Object.entries(chain)) {
    const vulnerability = report.vulnerabilities[name];
    const node = `node_modules/${name}`;
    const entry = lock.packages[node];
    requireCondition(isObject(vulnerability) && vulnerability.severity === "high"
      && vulnerability.isDirect === expected.direct && vulnerability.range === expected.range
      && sameStrings(vulnerability.nodes, [node])
      && sameStrings(vulnerability.effects, expected.effects),
    `${name}: a cadeia auditada mudou; revisão da exceção necessária.`);
    requireCondition(isObject(entry) && entry.version === expected.version && entry.dev === true
      && !entry.link && !root.dependencies?.[name] && !root.optionalDependencies?.[name],
    `${name}: versão ou classificação dev-only fora da exceção.`);
    const instances = Object.keys(lock.packages).filter((key) =>
      key === node || key.endsWith(`/node_modules/${name}`));
    requireCondition(sameStrings(instances, [node]), `${name}: instância não revisada no lockfile.`);
    if (expected.via !== null) {
      requireCondition(sameStrings(vulnerability.via, [expected.via])
        && typeof entry.dependencies?.[expected.via] === "string",
      `${name}: rota de dependência fora da exceção.`);
    } else {
      const [advisory] = vulnerability.via;
      requireCondition(vulnerability.via.length === 1 && isObject(advisory)
        && advisory.url === DEVELOPMENT_EXCEPTION.url && advisory.name === "braces"
        && advisory.dependency === "braces" && advisory.severity === "high"
        && advisory.range === "<=3.0.3",
      "braces: advisory fora da exceção de desenvolvimento.");
    }
    // npm's only suggested fix is the reviewed major downgrade. A newly available
    // fix changes this shape and must be reviewed rather than silently exempted.
    const fix = vulnerability.fixAvailable;
    requireCondition(isObject(fix) && fix.name === "eslint-config-next"
      && fix.version === "14.2.35" && fix.isSemVerMajor === true,
    `${name}: a correção disponível mudou; revisão da exceção necessária.`);
  }
  // No production ingress, extra development route, optional or peer consumer can
  // inherit this allowance merely by sharing a hoisted dependency.
  for (const [location, entry] of Object.entries(lock.packages)) {
    if (location === "") continue;
    requireCondition(isObject(entry), `Entrada inválida no lockfile: ${location}.`);
    for (const dependencies of [entry.dependencies, entry.optionalDependencies, entry.peerDependencies]) {
      if (dependencies === undefined) continue;
      requireCondition(isObject(dependencies), `Dependências inválidas em ${location}.`);
      for (const dependency of Object.keys(chain)) {
        if (!Object.hasOwn(dependencies, dependency)) continue;
        const consumer = Object.entries(chain).find(([, expected]) => expected.via === dependency)?.[0];
        requireCondition(location === `node_modules/${consumer}`
          && dependencies === entry.dependencies && entry.dev === true,
        `${dependency}: consumidor não revisado no lockfile (${location}).`);
      }
    }
  }
}

export function evaluateAudit(report, lock, { now = new Date(), exitCode } = {}) {
  const counts = validateReport(report, exitCode);
  const highCritical = Object.entries(report.vulnerabilities)
    .filter(([, value]) => value.severity === "high" || value.severity === "critical");
  if (highCritical.length === 0) return { developmentException: null, counts };
  const other = highCritical.filter(([name]) => !Object.hasOwn(chain, name));
  requireCondition(other.length === 0,
    `Vulnerabilidades high/critical bloqueadas: ${other.map(([name]) => name).join(", ")}.`);
  validateDevelopmentChain(report, lock, now);
  return { developmentException: DEVELOPMENT_EXCEPTION, counts };
}

export function runAuditGate({ projectDirectory = path.join(rootDirectory, "apps", "web"),
  execute = spawnSync, now = new Date() } = {}) {
  const lock = JSON.parse(readFileSync(path.join(projectDirectory, "package-lock.json"), "utf8"));
  const result = execute(process.platform === "win32" ? "npm.cmd" : "npm",
    ["audit", "--json", "--audit-level=high"], {
      cwd: projectDirectory, encoding: "utf8", timeout: 120_000,
      maxBuffer: 10 * 1024 * 1024, shell: process.platform === "win32",
    });
  requireCondition(!result.error && !result.signal && [0, 1].includes(result.status)
    && !result.stderr?.trim(),
  `Falha ao executar npm audit: ${result.error?.message ?? result.stderr?.trim() ?? result.signal ?? result.status}.`);
  const report = JSON.parse(result.stdout);
  return evaluateAudit(report, lock, { now, exitCode: result.status });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const result = runAuditGate();
    if (result.developmentException) {
      console.log(`Auditoria npm aprovada com exceção de desenvolvimento: ${DEVELOPMENT_EXCEPTION.advisory}, braces@3.0.3, cinco nós dev-only; vence ${DEVELOPMENT_EXCEPTION.expires} UTC.`);
    } else {
      console.log("Auditoria npm aprovada: nenhuma vulnerabilidade high/critical.");
    }
    const lower = result.counts.info + result.counts.low + result.counts.moderate;
    if (lower > 0) console.log(`${lower} achado(s) abaixo do nível high permanecem reportados.`);
  } catch (error) {
    console.error(`Auditoria npm bloqueada: ${error.message}`);
    process.exitCode = 1;
  }
}
