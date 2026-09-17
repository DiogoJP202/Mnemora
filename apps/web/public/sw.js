const CACHE_NAME = "mnemora-shell-v1";
const SHELL_ASSETS = [
  "/",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/icons/maskable-512.png",
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(SHELL_ASSETS)).then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((names) => Promise.all(names.filter((name) => name !== CACHE_NAME).map((name) => caches.delete(name))))
      .then(() => self.clients.claim()),
  );
});

function isPrivatePath(pathname) {
  return ["/api", "/app", "/admin"].some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`),
  );
}

function isStaticAsset(pathname) {
  return pathname.startsWith("/_next/static/")
    || pathname.startsWith("/icons/")
    || pathname === "/favicon.ico";
}

function canStore(response) {
  const cacheControl = response.headers.get("Cache-Control") ?? "";
  return response.ok
    && response.type === "basic"
    && !/(?:no-store|private)/i.test(cacheControl);
}

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin || isPrivatePath(url.pathname)) return;
  if (url.searchParams.has("_rsc") || request.headers.has("RSC") || request.headers.has("Next-Router-State-Tree")) return;

  if (url.pathname === "/" && url.search === "" && request.mode === "navigate") {
    event.respondWith(
      fetch(request)
        .then((response) => {
          if (canStore(response)) {
            const copy = response.clone();
            return caches.open(CACHE_NAME)
              .then((cache) => cache.put(request, copy))
              .then(() => response);
          }
          return response;
        })
        .catch(() => caches.match("/")),
    );
    return;
  }

  if (!isStaticAsset(url.pathname)) return;

  event.respondWith(
    caches.match(request).then((cached) => {
      if (cached) return cached;
      return fetch(request).then((response) => {
        if (canStore(response)) {
          const copy = response.clone();
          return caches.open(CACHE_NAME)
            .then((cache) => cache.put(request, copy))
            .then(() => response);
        }
        return response;
      });
    }),
  );
});
