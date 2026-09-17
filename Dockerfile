FROM node:24.19.0-alpine3.23 AS web-build

WORKDIR /src/apps/web

ARG MNEMORA_DEMO=false

COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci

COPY apps/web/ ./
ENV NEXT_TELEMETRY_DISABLED=1 \
    API_BASE_URL=http://127.0.0.1:5100 \
    MNEMORA_DEMO=$MNEMORA_DEMO
RUN npm run build


FROM mcr.microsoft.com/dotnet/sdk:10.0.400-alpine3.23 AS api-build

WORKDIR /src

COPY global.json Mnemora.slnx ./
COPY src/Mnemora.Api/Mnemora.Api.csproj src/Mnemora.Api/
COPY src/Mnemora.Application/Mnemora.Application.csproj src/Mnemora.Application/
COPY src/Mnemora.Domain/Mnemora.Domain.csproj src/Mnemora.Domain/
COPY src/Mnemora.Infrastructure/Mnemora.Infrastructure.csproj src/Mnemora.Infrastructure/
RUN dotnet restore src/Mnemora.Api/Mnemora.Api.csproj

COPY src/ src/
RUN dotnet publish src/Mnemora.Api/Mnemora.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /out/api \
    /p:UseAppHost=false


FROM node:24.19.0-alpine3.23 AS node-runtime


FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.23 AS runtime

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://127.0.0.1:5100 \
    ConnectionStrings__Default="Data Source=/var/data/mnemora.db" \
    Mnemora__DataProtectionKeysPath=/var/data/keys \
    NEXT_TELEMETRY_DISABLED=1 \
    NODE_ENV=production \
    PORT=3000

WORKDIR /app

COPY --from=node-runtime /usr/local/ /usr/local/
COPY --from=api-build --chown=1654:1654 /out/api/ /app/api/
COPY --from=web-build --chown=1654:1654 /src/apps/web/.next/standalone/ /app/web/
COPY --from=web-build --chown=1654:1654 /src/apps/web/.next/static/ /app/web/.next/static/
COPY --from=web-build --chown=1654:1654 /src/apps/web/public/ /app/web/public/
COPY --chown=1654:1654 deploy/render/entrypoint.sh /app/entrypoint.sh

RUN chmod 0555 /app/entrypoint.sh \
    && mkdir -p /var/data \
    && chown 1654:1654 /var/data

USER 1654:1654

EXPOSE 3000
VOLUME ["/var/data"]

HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||'3000')+'/api/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

ENTRYPOINT ["/app/entrypoint.sh"]
