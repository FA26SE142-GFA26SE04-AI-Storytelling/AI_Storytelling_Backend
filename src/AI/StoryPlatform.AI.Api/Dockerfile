# AI-only build: Core and AWS infrastructure are not part of this image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Shared/StoryPlatform.Contracts/StoryPlatform.Contracts.csproj src/Shared/StoryPlatform.Contracts/
COPY src/AI/StoryPlatform.AI.Domain/StoryPlatform.AI.Domain.csproj src/AI/StoryPlatform.AI.Domain/
COPY src/AI/StoryPlatform.AI.Application/StoryPlatform.AI.Application.csproj src/AI/StoryPlatform.AI.Application/
COPY src/AI/StoryPlatform.AI.Infrastructure/StoryPlatform.AI.Infrastructure.csproj src/AI/StoryPlatform.AI.Infrastructure/
COPY src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj src/AI/StoryPlatform.AI.Api/
RUN dotnet restore src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj
COPY src/Shared/StoryPlatform.Contracts/ src/Shared/StoryPlatform.Contracts/
COPY src/AI/ src/AI/
RUN dotnet publish src/AI/StoryPlatform.AI.Api/StoryPlatform.AI.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS ai-api
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /out/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "StoryPlatform.AI.Api.dll"]
