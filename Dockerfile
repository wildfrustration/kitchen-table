# syntax=docker/dockerfile:1
# Kitchen Table: one image with the web app, the API and the data CLI.

# --- web: build the React app ------------------------------------------------------------
FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx tsc -b && npx vite build --outDir /out/wwwroot --emptyOutDir

# --- api + cli: restore, then publish ----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json PlanD.sln Directory.Build.props ./
COPY src/PlanD.Engine/PlanD.Engine.csproj src/PlanD.Engine/
COPY src/PlanD.Data/PlanD.Data.csproj src/PlanD.Data/
COPY src/PlanD.Api/PlanD.Api.csproj src/PlanD.Api/
COPY src/PlanD.Cli/PlanD.Cli.csproj src/PlanD.Cli/
RUN dotnet restore src/PlanD.Api/PlanD.Api.csproj && dotnet restore src/PlanD.Cli/PlanD.Cli.csproj
COPY src/ src/
# The OpenAPI document is generated for the web app's types during development, not in the image.
RUN dotnet publish src/PlanD.Api -c Release -o /out/api --no-restore -p:OpenApiGenerateDocuments=false \
 && dotnet publish src/PlanD.Cli -c Release -o /out/cli --no-restore

# --- runtime -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=build /out/cli ./cli/
COPY --from=web /out/wwwroot ./wwwroot/
COPY docker/entrypoint.sh /usr/local/bin/kt
RUN chmod +x /usr/local/bin/kt
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["kt"]
CMD ["serve"]
