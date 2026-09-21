# src/Core/StoryPlatform.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj src/Core/StoryPlatform.Api/
COPY src/Core/StoryPlatform.Application/StoryPlatform.Application.csproj src/Core/StoryPlatform.Application/
COPY src/Core/StoryPlatform.Domain/StoryPlatform.Domain.csproj src/Core/StoryPlatform.Domain/
COPY src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj src/Core/StoryPlatform.Infrastructure/
COPY src/Shared/StoryPlatform.Contracts/StoryPlatform.Contracts.csproj src/Shared/StoryPlatform.Contracts/

RUN dotnet restore src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj

COPY src/Core/StoryPlatform.Api/ src/Core/StoryPlatform.Api/
COPY src/Core/StoryPlatform.Application/ src/Core/StoryPlatform.Application/
COPY src/Core/StoryPlatform.Domain/ src/Core/StoryPlatform.Domain/
COPY src/Core/StoryPlatform.Infrastructure/ src/Core/StoryPlatform.Infrastructure/
COPY src/Shared/StoryPlatform.Contracts/ src/Shared/StoryPlatform.Contracts/

RUN dotnet publish src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StoryPlatform.Api.dll"]
