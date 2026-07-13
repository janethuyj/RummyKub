# ---- Stage 1: build the React client ----
FROM node:20-alpine AS client
WORKDIR /client
COPY client/package*.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

# ---- Stage 2: build & publish the ASP.NET Core server ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS server
WORKDIR /src
COPY RummyKub.sln ./
COPY server/ ./server/
RUN dotnet restore server/Rummikub.Server/Rummikub.Server.csproj
# Drop the built client into wwwroot so the server serves it as static files.
COPY --from=client /client/dist ./server/Rummikub.Server/wwwroot
RUN dotnet publish server/Rummikub.Server/Rummikub.Server.csproj -c Release -o /app

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=server /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rummikub.Server.dll"]
