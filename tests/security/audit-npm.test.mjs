import assert from "node:assert/strict";
import test from "node:test";
import { DEVELOPMENT_EXCEPTION, evaluateAudit, runAuditGate } from "../../scripts/audit-npm.mjs";

const reviewedDate = new Date("2026-10-09T12:00:00.000Z");
const names = ["eslint-config-next", "@next/eslint-plugin-next", "fast-glob", "micromatch", "braces"];
const versions = ["16.3.8", "16.3.8", "3.3.1", "4.0.8", "3.0.3"];
const ranges = [">=14.3.0-canary.0", ">=14.3.0-canary.0", "*", ">=0.2.0", "*"];

function advisory(name = "braces", severity = "high", url = DEVELOPMENT_EXCEPTION.url) {
  return { source: 1240992, name, dependency: name, title: "Stack exhaustion",
    url, severity, cwe: ["CWE-674"], cvss: { score: 7.5, vectorString: "CVSS:3.1" }, range: "<=3.0.3" };
}

function reportFor(vulnerabilities) {
  const counts = { info: 0, low: 0, moderate: 0, high: 0, critical: 0, total: 0 };
  for (const value of Object.values(vulnerabilities)) { counts[value.severity] += 1; counts.total += 1; }
  return { auditReportVersion: 2, vulnerabilities,
    metadata: { vulnerabilities: counts, dependencies: { prod: 1, dev: 5, total: 6 } } };
}

function fixtures() {
  const packages = { "": { dependencies: { next: "16.3.8" }, devDependencies: { "eslint-config-next": "16.3.8" } } };
  const vulnerabilities = {};
  for (const [index, name] of names.entries()) {
    const next = names[index + 1];
    packages[`node_modules/${name}`] = { version: versions[index], dev: true,
      ...(next ? { dependencies: { [next]: versions[index + 1] } } : {}) };
    vulnerabilities[name] = { name, severity: "high", isDirect: index === 0,
      via: next ? [next] : [advisory()], effects: index === 0 ? [] : [names[index - 1]],
      range: ranges[index], nodes: [`node_modules/${name}`],
      fixAvailable: { name: "eslint-config-next", version: "14.2.35", isSemVerMajor: true } };
  }
  return { report: reportFor(vulnerabilities), lock: { lockfileVersion: 3, packages } };
}

function evaluate(report, lock, now = reviewedDate, exitCode = 1) {
  return evaluateAudit(report, lock, { now, exitCode });
}

test("accepts only the reviewed exact development chain and reports its exception", () => {
  const { report, lock } = fixtures();
  const result = evaluate(report, lock);
  assert.equal(result.developmentException.advisory, "GHSA-vfj7-8cjw-p6xm");
  assert.equal(result.counts.high, 5);
});

test("accepts a clean audit without applying an exception after expiry", () => {
  const result = evaluate(reportFor({}), undefined, new Date("2026-11-10T00:00:00Z"), 0);
  assert.equal(result.developmentException, null);
});

test("retains audit-level high behavior for structurally valid moderate advisories", () => {
  const report = reportFor({ example: { name: "example", severity: "moderate", isDirect: true,
    via: [advisory("example", "moderate", "https://github.com/advisories/GHSA-abcd-abcd-abcd")],
    effects: [], range: "*", nodes: ["node_modules/example"], fixAvailable: true } });
  assert.equal(evaluate(report, undefined, reviewedDate, 0).counts.moderate, 1);
});

for (const severity of ["high", "critical"]) {
  test(`blocks another ${severity} advisory even alongside the exception`, () => {
    const { report, lock } = fixtures();
    report.vulnerabilities.other = { name: "other", severity, isDirect: true,
      via: [advisory("other", severity, "https://github.com/advisories/GHSA-abcd-abcd-abcd")],
      effects: [], range: "*", nodes: ["node_modules/other"], fixAvailable: true };
    assert.throws(() => evaluate(reportFor(report.vulnerabilities), lock), /high\/critical bloqueadas/);
  });
}

for (const [index, name] of names.entries()) {
  test(`blocks an unreviewed version of ${name}`, () => {
    const { report, lock } = fixtures();
    lock.packages[`node_modules/${name}`].version = `${versions[index]}-changed`;
    assert.throws(() => evaluate(report, lock), /versão ou classificação dev-only/);
  });
  test(`blocks ${name} when it is not exclusively dev-only`, () => {
    const { report, lock } = fixtures();
    delete lock.packages[`node_modules/${name}`].dev;
    assert.throws(() => evaluate(report, lock), /versão ou classificação dev-only/);
  });
}

const mutations = [
  ["another braces advisory", ({ report }) => { report.vulnerabilities.braces.via[0].url = "https://github.com/advisories/GHSA-unknown"; }, /advisory fora/],
  ["additional braces advisory", ({ report }) => { report.vulnerabilities.braces.via.push(advisory()); }, /advisory fora/],
  ["critical severity for the reviewed package", ({ report }) => { report.vulnerabilities.braces.severity = "critical"; }, /cadeia auditada mudou/],
  ["missing propagated finding", ({ report }) => { delete report.vulnerabilities["eslint-config-next"]; }, /cadeia auditada mudou/],
  ["unknown affected node", ({ report }) => { report.vulnerabilities.braces.nodes.push("node_modules/other/node_modules/braces"); }, /cadeia auditada mudou/],
  ["unknown dependency route", ({ report }) => { report.vulnerabilities.micromatch.via = ["fast-glob"]; }, /rota de dependência/],
  ["unknown advisory reference", ({ report }) => { report.vulnerabilities.micromatch.via = ["unknown"]; }, /referência de advisory desconhecida/],
  ["new available patch", ({ report }) => { report.vulnerabilities.braces.fixAvailable = true; }, /correção disponível mudou/],
  ["another installed instance", ({ lock }) => { lock.packages["node_modules/other/node_modules/braces"] = { version: "3.0.3", dev: true }; }, /instância não revisada/],
  ["production root dependency", ({ lock }) => { lock.packages[""].dependencies.braces = "3.0.3"; }, /dev-only/],
  ["extra consumer", ({ lock }) => { lock.packages["node_modules/other"] = { version: "1.0.0", dev: true, dependencies: { braces: "3.0.3" } }; }, /consumidor não revisado/],
  ["production consumer", ({ lock }) => { lock.packages["node_modules/runtime"] = { version: "1.0.0", dependencies: { micromatch: "4.0.8" } }; }, /consumidor não revisado/],
  ["unknown report version", ({ report }) => { report.auditReportVersion = 3; }, /Formato desconhecido/],
  ["unknown report field", ({ report }) => { report.error = { code: "ENETUNREACH" }; }, /estrutura desconhecida/],
  ["unknown advisory structure", ({ report }) => { report.vulnerabilities.braces.via[0].unexpected = true; }, /estrutura desconhecida/],
  ["invalid advisory value", ({ report }) => { report.vulnerabilities.braces.via = [42]; }, /objeto esperado/],
  ["unknown lockfile version", ({ lock }) => { lock.lockfileVersion = 4; }, /Lockfile desconhecido/],
];

for (const [label, mutate, expectedError] of mutations) {
  test(`fails closed for ${label}`, () => {
    const fixture = fixtures();
    mutate(fixture);
    // Keep counts consistent when the test intentionally changes the findings.
    if (label === "critical severity for the reviewed package" || label === "missing propagated finding")
      fixture.report = reportFor(fixture.report.vulnerabilities);
    assert.throws(() => evaluate(fixture.report, fixture.lock), expectedError);
  });
}

test("blocks at the UTC expiry boundary and rejects an invalid clock", () => {
  const { report, lock } = fixtures();
  assert.ok(evaluate(report, lock, new Date("2026-11-08T23:59:59.999Z")).developmentException);
  assert.throws(() => evaluate(report, lock, new Date("2026-11-09T00:00:00.000Z")), /venceu/);
  assert.throws(() => evaluate(report, lock, new Date("invalid")), /Data de auditoria inválida/);
});

test("blocks inconsistent audit counts and an unexpected success exit code", () => {
  const { report, lock } = fixtures();
  report.metadata.vulnerabilities.high = 0;
  assert.throws(() => evaluate(report, lock), /Contagem inconsistente/);
  assert.throws(() => evaluate(fixtures().report, lock, reviewedDate, 0), /código inesperado/);
});

test("runner blocks malformed JSON, npm errors, network diagnostics and signals", () => {
  const cases = [
    { status: 1, stdout: "not json", stderr: "" },
    { status: 1, stdout: JSON.stringify({ error: { code: "ENETUNREACH" } }), stderr: "" },
    { status: 1, stdout: JSON.stringify(fixtures().report), stderr: "network error" },
    { status: 2, stdout: JSON.stringify(fixtures().report), stderr: "" },
    { status: null, stdout: "", stderr: "", error: new Error("spawn failed") },
    { status: 1, stdout: JSON.stringify(fixtures().report), stderr: "", signal: "SIGTERM" },
  ];
  for (const result of cases) assert.throws(() => runAuditGate({ now: reviewedDate, execute: () => result }));
});

test("runner invokes a full npm audit with the high threshold", () => {
  const result = runAuditGate({ now: reviewedDate, execute: (_command, args, options) => {
    assert.deepEqual(args, ["audit", "--json", "--audit-level=high"]);
    assert.ok(options.cwd.endsWith(`apps${process.platform === "win32" ? "\\" : "/"}web`));
    return { status: 1, stdout: JSON.stringify(fixtures().report), stderr: "" };
  } });
  assert.equal(result.developmentException.advisory, DEVELOPMENT_EXCEPTION.advisory);
});
