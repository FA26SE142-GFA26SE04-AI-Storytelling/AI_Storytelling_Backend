# syntax=docker/dockerfile:1.7

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /workspace

# Copy project files first so NuGet restore can be cached independently from source changes.
COPY src/Shared/StoryPlatform.Contracts/StoryPlatform.Contracts.csproj src/Shared/StoryPlatform.Contracts/
COPY src/Core/StoryPlatform.Domain/StoryPlatform.Domain.csproj src/Core/StoryPlatform.Domain/
COPY src/Core/StoryPlatform.Application/StoryPlatform.Application.csproj src/Core/StoryPlatform.Application/
COPY src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj src/Core/StoryPlatform.Infrastructure/
COPY src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj src/Core/StoryPlatform.Api/
COPY src/AI/StoryPlatform.AI.Domain/StoryPlatform.AI.Domain.csproj src/AI/StoryPlatform.AI.Domain/
COPY src/AI/StoryPlatform.AI.Application/StoryPlatform.AI.Application.csproj src/AI/StoryPlatform.AI.Application/
COPY src/AI/StoryPlatform.AI.Infrastructure/StoryPlatform.AI.Infrastructure.csproj src/AI/StoryPlatform.AI.Infrastructure/
COPY src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj src/AI/StoryPlatform.AI.Api/

RUN dotnet restore src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj
RUN dotnet restore src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj

COPY src/ src/

FROM restore AS publish-core
RUN dotnet publish src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /artifacts/core \
    /p:UseAppHost=false

FROM restore AS publish-ai
RUN dotnet publish src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /artifacts/ai \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID

FROM runtime AS core-api
COPY --from=publish-core /artifacts/core/ ./
ENTRYPOINT ["dotnet", "StoryPlatform.Api.dll"]

FROM runtime AS ai-api
COPY --from=publish-ai /artifacts/ai/ ./
ENTRYPOINT ["dotnet", "StoryPlatform.AI.Api.dll"]
