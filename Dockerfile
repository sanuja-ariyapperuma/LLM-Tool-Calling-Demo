# syntax=docker/dockerfile:1

# 1. css: build the Tailwind stylesheet (Node is needed only here)
FROM node:22-alpine AS css
WORKDIR /web
COPY src/FinGuitar.Web/package.json src/FinGuitar.Web/package-lock.json ./
RUN npm ci
COPY src/FinGuitar.Web ./
RUN npm run css:build

# 2. build: restore (cached), compile, publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FinGuitar.sln ./
COPY src/FinGuitar.Web/FinGuitar.Web.csproj src/FinGuitar.Web/
COPY tests/FinGuitar.Tests/FinGuitar.Tests.csproj tests/FinGuitar.Tests/
RUN dotnet restore
COPY src ./src
COPY tests ./tests
COPY --from=css /web/wwwroot/css/app.css src/FinGuitar.Web/wwwroot/css/app.css
RUN dotnet publish src/FinGuitar.Web/FinGuitar.Web.csproj -c Release -o /out --no-restore

# 3. test: used only by `docker build --target test .`
FROM build AS test
RUN dotnet test -c Release --no-restore

# 4. runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FinGuitar.Web.dll"]
