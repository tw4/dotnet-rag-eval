# syntax=docker/dockerfile:1

# ---- build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props RagEval.slnx .editorconfig ./
COPY src/RagEval.Core/RagEval.Core.csproj src/RagEval.Core/
COPY src/RagEval.Ingestion/RagEval.Ingestion.csproj src/RagEval.Ingestion/
COPY src/RagEval.Api/RagEval.Api.csproj src/RagEval.Api/
RUN dotnet restore src/RagEval.Api/RagEval.Api.csproj
COPY src/ src/
RUN dotnet publish src/RagEval.Api/RagEval.Api.csproj -c Release -o /app --no-restore

# ---- runtime: chiseled image, no shell, non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RagEval.Api.dll"]
