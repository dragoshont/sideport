# syntax=docker/dockerfile:1
#
# Sideport — the pure-.NET "brain" (design §4). Ships as a multi-arch Linux
# image (linux/amd64 + linux/arm64). The two non-managed sidecars — anisette
# and the signer binary — are NOT baked in here; they run alongside via
# compose.yaml so they stay behind the IAnisetteProvider / ISigner seams.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY Sideport.slnx ./
COPY src/ ./src/
RUN dotnet restore src/Sideport.Api/Sideport.Api.csproj
RUN dotnet publish src/Sideport.Api/Sideport.Api.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# The signer binary (zsign/rcodesign) is mounted in by compose at this path.
ENV Sideport__Signer__BinaryPath=/opt/sideport/zsign
ENV Sideport__Anisette__BaseUrl=http://anisette:6969/
ENTRYPOINT ["dotnet", "Sideport.Api.dll"]
