# Build context is the repository root: docker build -f src/Cosmorph.Api/Dockerfile .
# The public container serves the built Observatory and the stateless JSON API.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Cosmorph.Domain/Cosmorph.Domain.csproj src/Cosmorph.Domain/
COPY src/Cosmorph.Application/Cosmorph.Application.csproj src/Cosmorph.Application/
COPY src/Cosmorph.Infrastructure/Cosmorph.Infrastructure.csproj src/Cosmorph.Infrastructure/
COPY src/Cosmorph.Api/Cosmorph.Api.csproj src/Cosmorph.Api/
RUN dotnet restore src/Cosmorph.Api/Cosmorph.Api.csproj
COPY src/Cosmorph.Domain/ src/Cosmorph.Domain/
COPY src/Cosmorph.Application/ src/Cosmorph.Application/
COPY src/Cosmorph.Infrastructure/ src/Cosmorph.Infrastructure/
COPY src/Cosmorph.Api/ src/Cosmorph.Api/
RUN dotnet publish src/Cosmorph.Api/Cosmorph.Api.csproj -c Release -o /app/publish --no-restore

FROM node:22-bookworm-slim AS frontend
WORKDIR /web
COPY src/Cosmorph.Web/package.json src/Cosmorph.Web/package-lock.json ./
RUN npm ci
COPY src/Cosmorph.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=backend /app/publish ./
COPY --from=frontend /web/dist ./wwwroot/
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cosmorph.Api.dll"]
