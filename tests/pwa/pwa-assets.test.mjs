import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import path from "node:path";
import test from "node:test";
import vm from "node:vm";

const repositoryRoot = path.resolve(import.meta.dirname, "..", "..");
const webRoot = path.join(repositoryRoot, "apps", "web");
const publicRoot = path.join(webRoot, "public");
const manifestPath = path.join(webRoot, "src", "app", "manifest.ts");
const serviceWorkerPath = path.join(publicRoot, "sw.js");

function readPngDimensions(filePath) {
  const contents = readFileSync(filePath);
  const pngSignature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

  assert.ok(
    contents.subarray(0, pngSignature.length).equals(pngSignature),
    `${path.relative(repositoryRoot, filePath)} deve ser um PNG valido`,
  );
  assert.equal(contents.subarray(12, 16).toString("ascii"), "IHDR");

  return {
    width: contents.readUInt32BE(16),
    height: contents.readUInt32BE(20),
  };
}

async function loadManifest() {
  assert.ok(existsSync(manifestPath), "apps/web/src/app/manifest.ts deve existir");

  const moduleUrl = `${pathToFileURL(manifestPath).href}?test=${Date.now()}`;
  const manifestModule = await import(moduleUrl);
  assert.equal(typeof manifestModule.default, "function", "manifest.ts deve exportar uma funcao padrao");

  return manifestModule.default();
}

function createServiceWorkerHarness() {
  assert.ok(existsSync(serviceWorkerPath), "apps/web/public/sw.js deve existir");

  const listeners = new Map();
  const cacheCalls = [];
  const networkCalls = [];

  const cache = {
    add: async (request) => {
      cacheCalls.push({ method: "add", request });
    },
    addAll: async (requests) => {
      cacheCalls.push({ method: "addAll", requests: [...requests] });
    },
    match: async (request) => {
      cacheCalls.push({ method: "cache.match", request });
      return undefined;
    },
    put: async (request) => {
      cacheCalls.push({ method: "put", request });
    },
  };

  const caches = {
    delete: async (name) => {
      cacheCalls.push({ method: "delete", name });
      return true;
    },
    keys: async () => [],
    match: async (request) => {
      cacheCalls.push({ method: "caches.match", request });
      return undefined;
    },
    open: async (name) => {
      cacheCalls.push({ method: "open", name });
      return cache;
    },
  };

  const self = {
    addEventListener(type, listener) {
      const registered = listeners.get(type) ?? [];
      registered.push(listener);
      listeners.set(type, registered);
    },
    clients: {
      claim: async () => undefined,
    },
    location: new URL("https://mnemora.test/"),
    skipWaiting: async () => undefined,
  };

  const context = vm.createContext({
    URL,
    Request,
    Response,
    Headers,
    caches,
    clients: self.clients,
    console,
    fetch: async (request) => {
      networkCalls.push(request);
      return new Response("network", {
        status: 200,
        headers: { "Content-Type": "text/plain" },
      });
    },
    self,
    setTimeout,
    clearTimeout,
  });

  vm.runInContext(readFileSync(serviceWorkerPath, "utf8"), context, {
    filename: serviceWorkerPath,
  });

  async function dispatch(type, init = {}) {
    const pending = [];
    const responses = [];
    const event = {
      ...init,
      respondWith(value) {
        const response = Promise.resolve(value);
        responses.push(response);
        pending.push(response);
      },
      waitUntil(value) {
        pending.push(Promise.resolve(value));
      },
    };

    for (const listener of listeners.get(type) ?? []) {
      pending.push(Promise.resolve(listener(event)));
    }

    await Promise.allSettled(pending);
    return { event, responses };
  }

  return { cacheCalls, dispatch, listeners, networkCalls };
}

function requestPath(request) {
  if (typeof request === "string") {
    return new URL(request, "https://mnemora.test").pathname;
  }

  return new URL(request.url).pathname;
}

test("manifesto oferece metadados de instalacao e icones locais validos", async () => {
  const manifest = await loadManifest();

  assert.equal(manifest.name, "Mnemora");
  assert.ok(manifest.short_name, "short_name e obrigatorio");
  assert.equal(manifest.lang, "pt-BR");
  assert.equal(manifest.start_url, "/");
  assert.match(manifest.display, /^(standalone|minimal-ui)$/);
  assert.ok(manifest.theme_color, "theme_color e obrigatorio");
  assert.ok(manifest.background_color, "background_color e obrigatorio");
  assert.ok(Array.isArray(manifest.icons), "icons deve ser uma lista");

  for (const requiredSize of [192, 512]) {
    const icon = manifest.icons.find(({ sizes }) =>
      String(sizes)
        .split(/\s+/)
        .includes(`${requiredSize}x${requiredSize}`),
    );

    assert.ok(icon, `o manifesto deve declarar um icone ${requiredSize}x${requiredSize}`);
    assert.match(icon.src, /^\/icons\/[^?#]+\.png(?:[?#].*)?$/i);
    assert.equal(icon.type, "image/png");

    const iconPath = path.join(publicRoot, new URL(icon.src, "https://mnemora.test").pathname);
    assert.ok(
      existsSync(iconPath),
      `${path.relative(repositoryRoot, iconPath)} deve existir`,
    );
    assert.deepEqual(readPngDimensions(iconPath), {
      width: requiredSize,
      height: requiredSize,
    });
  }
});

test("service worker nao inclui rotas privadas no pre-cache", async () => {
  const harness = createServiceWorkerHarness();

  assert.ok(harness.listeners.has("install"), "sw.js deve registrar o evento install");
  await harness.dispatch("install");

  const preCachedPaths = harness.cacheCalls
    .filter(({ method }) => method === "add" || method === "addAll")
    .flatMap(({ method, request, requests }) => (method === "addAll" ? requests : [request]))
    .map(requestPath);

  for (const cachedPath of preCachedPaths) {
    assert.doesNotMatch(
      cachedPath,
      /^\/(?:api|app|admin)(?:\/|$)/,
      `${cachedPath} nao pode fazer parte do pre-cache`,
    );
  }
});

test("service worker nunca consulta nem grava cache para /api, /app e /admin", async () => {
  const privateUrls = [
    "/api",
    "/api/books?query=arquivo",
    "/app",
    "/app/library",
    "/app/books/123?tab=timeline",
    "/admin",
    "/admin/books/123",
  ];

  for (const privateUrl of privateUrls) {
    const harness = createServiceWorkerHarness();
    assert.ok(harness.listeners.has("fetch"), "sw.js deve registrar o evento fetch");

    await harness.dispatch("fetch", {
      request: new Request(new URL(privateUrl, "https://mnemora.test"), {
        method: "GET",
      }),
    });

    assert.deepEqual(
      harness.cacheCalls,
      [],
      `${privateUrl} nao pode consultar, abrir nem gravar caches`,
    );
  }
});

test("service worker ignora paginas e arquivos fora do shell publico", async () => {
  const ignoredUrls = [
    "/login",
    "/forgot-password",
    "/?origem=campanha",
    "/uploads/anotacao-privada.png",
  ];

  for (const ignoredUrl of ignoredUrls) {
    const harness = createServiceWorkerHarness();
    await harness.dispatch("fetch", {
      request: new Request(new URL(ignoredUrl, "https://mnemora.test"), {
        method: "GET",
      }),
    });

    assert.deepEqual(
      harness.cacheCalls,
      [],
      `${ignoredUrl} deve seguir direto para a rede sem consultar caches`,
    );
  }
});
