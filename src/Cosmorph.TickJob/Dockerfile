# Build context is the repository root: docker build -f src/Cosmorph.TickJob/Dockerfile .
# The TickJob runs as a scheduled Container Apps Job; one execution drains a bounded batch and exits.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Cosmorph.Domain/Cosmorph.Domain.csproj src/Cosmorph.Domain/
COPY src/Cosmorph.Application/Cosmorph.Application.csproj src/Cosmorph.Application/
COPY src/Cosmorph.Infrastructure/Cosmorph.Infrastructure.csproj src/Cosmorph.Infrastructure/
COPY src/Cosmorph.TickJob/Cosmorph.TickJob.csproj src/Cosmorph.TickJob/
RUN dotnet restore src/Cosmorph.TickJob/Cosmorph.TickJob.csproj
COPY src/Cosmorph.Domain/ src/Cosmorph.Domain/
COPY src/Cosmorph.Application/ src/Cosmorph.Application/
COPY src/Cosmorph.Infrastructure/ src/Cosmorph.Infrastructure/
COPY src/Cosmorph.TickJob/ src/Cosmorph.TickJob/
RUN dotnet publish src/Cosmorph.TickJob/Cosmorph.TickJob.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Cosmorph.TickJob.dll"]
