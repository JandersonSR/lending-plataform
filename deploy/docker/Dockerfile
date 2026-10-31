# syntax=docker/dockerfile:1
#
# Imagem única do CreditFlow. A MESMA imagem sobe como api-geral, api-simulacao,
# api-pagamentos ou worker-pagamentos: quem decide é CREDITFLOW__MODULOS e CREDITFLOW__PAPEL.
#
# Build (na raiz do repositório, que é o contexto):
#   docker buildx build -f deploy/docker/Dockerfile -t creditflow:dev .
# Multi-arquitetura:
#   docker buildx build -f deploy/docker/Dockerfile --platform linux/amd64,linux/arm64 -t <registry>/creditflow/creditflow:<tag> --push .
#
# Explicação linha por linha: curso CreditFlow, módulo 7, lição 7.1.

ARG DOTNET_VERSION=10.0

# ---------------------------------------------------------------------------
# Estágio 1: só os arquivos que definem dependências.
# Copia tudo e apaga o que NÃO é .csproj/.slnx/props/global.json/nuget.config.
# O conteúdo final deste estágio só muda quando uma dependência muda, então o
# "dotnet restore" do estágio 2 vem do cache quando você mexe só em código C#.
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS dependencias
WORKDIR /src
COPY . .
RUN find . -type f \
      ! -name '*.csproj' ! -name '*.slnx' ! -name 'Directory.Build.props' \
      ! -name 'Directory.Packages.props' ! -name 'global.json' ! -name 'nuget.config' \
      -delete \
 && find . -type d -empty -delete

# ---------------------------------------------------------------------------
# Estágio 2: restore + publish com o SDK (roda na arquitetura de quem compila).
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH
ARG CONFIGURATION=Release
ARG VERSAO=0.0.0-local
WORKDIR /src

COPY --from=dependencias /src ./
# Cache do NuGet montado no build: sobrevive entre builds na mesma máquina/runner
# e não entra em nenhuma camada da imagem.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Host/CreditFlow.Host/CreditFlow.Host.csproj -a $TARGETARCH

COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Host/CreditFlow.Host/CreditFlow.Host.csproj \
      -c $CONFIGURATION -a $TARGETARCH --no-restore \
      -o /app \
      -p:UseAppHost=false \
      -p:Version=$VERSAO \
      -p:InformationalVersion=$VERSAO

# ---------------------------------------------------------------------------
# Estágio 3: runtime. Chiseled = sem shell, sem gerenciador de pacotes, sem root.
# "-extra" traz ICU e tzdata: o CreditFlow formata dinheiro em pt-BR e usa
# America/Sao_Paulo; com a imagem chiseled comum (modo invariante) isso quebra.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-extra AS final
ARG VERSAO=0.0.0-local
ARG REVISAO=desconhecida

LABEL org.opencontainers.image.title="creditflow" \
      org.opencontainers.image.source="https://github.com/JandersonSR/lending-plataform" \
      org.opencontainers.image.version="${VERSAO}" \
      org.opencontainers.image.revision="${REVISAO}" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app
# Sem --chown de propósito: os arquivos ficam do root e só leitura para o app.
# Um processo comprometido não consegue reescrever as próprias DLLs.
COPY --from=build /app ./

# Usuário sem privilégio (UID 1654, já existe na imagem). O Kubernetes reforça
# com runAsNonRoot no securityContext do chart.
USER $APP_UID

# Server GC já é o padrão do ASP.NET Core e, desde o .NET 9, vem com DATAS
# (o heap se adapta ao tamanho do contêiner). Não precisa de variável de GC aqui.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=America/Sao_Paulo \
    CreditFlow__Versao=${VERSAO}

EXPOSE 8080

# Só para "docker run"/compose: o Kubernetes IGNORA o HEALTHCHECK e usa as probes.
# Sem shell nem curl na imagem chiseled, o próprio Host faz a verificação
# (argumento --healthcheck: GET http://localhost:8080/healthz/live e sai 0 ou 1).
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD ["dotnet", "CreditFlow.Host.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "CreditFlow.Host.dll"]
