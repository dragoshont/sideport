# syntax=docker/dockerfile:1
#
# Sideport — the pure-.NET "brain" (design §4) plus the proven zsign signer.
#
# The image bundles zsign (the iOS-17+/26-valid signer, GLESign/zsign #391)
# copied from our own altserver-linux image, so the running pod is self-contained
# for signing. anisette stays a separate sidecar behind the IAnisetteProvider
# seam (it's closed-source ADI code — a license + process boundary).
#
# zsign is a static amd64 ELF, so this image is linux/amd64 (the homelab node's
# arch). Building arm64 additionally would require an arm64 zsign build.

ARG DOTNET_VERSION=10.0
ARG ALTSERVER_IMAGE=ghcr.io/dragoshont/altserver-linux:latest-main

# --- Stage 1: the proven zsign signer (just a source for the COPY) -----------
FROM ${ALTSERVER_IMAGE} AS signer

# --- Stage 2: build + publish the .NET app -----------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY Sideport.slnx ./
COPY src/ ./src/
RUN dotnet restore src/Sideport.Api/Sideport.Api.csproj
RUN dotnet publish src/Sideport.Api/Sideport.Api.csproj \
        -c Release -o /app --no-restore /p:UseAppHost=false

# --- Stage 3: runtime --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# App + the signer binary (world-readable/executable so an arbitrary non-root
# UID — the k8s securityContext runs as 1000 — can read/exec them).
COPY --from=build --chmod=0755 /app ./
COPY --from=signer --chmod=0755 /usr/local/bin/zsign /opt/sideport/zsign

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    Sideport__Signer__BinaryPath=/opt/sideport/zsign \
    Sideport__Anisette__Url=http://anisette:6969/

# Run as a non-root UID by default (k8s overrides to 1000; this keeps `docker run`
# non-root too). The aspnet base ships an unprivileged user.
USER 1000:1000

ENTRYPOINT ["dotnet", "Sideport.Api.dll"]
