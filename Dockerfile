# Client build stage — builds the React app so the .NET stage doesn't need Node.js.
FROM node:20 AS client-build
WORKDIR /app
COPY client/ ./client/
WORKDIR /app/client
RUN npm install --no-audit --no-fund
RUN npm run build
# Vite outputs to ../src/HstReceipts.Web/wwwroot/client (see client/vite.config.ts)

# .NET build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files
COPY . .

# Overlay the freshly-built client assets from the client-build stage
COPY --from=client-build /app/src/HstReceipts.Web/wwwroot/client ./src/HstReceipts.Web/wwwroot/client

# Build the project
RUN dotnet build src/HstReceipts.Web/HstReceipts.Web.csproj -c Release

# Publish the project (skip the csproj's own npm build target — client is already built above)
RUN dotnet publish src/HstReceipts.Web/HstReceipts.Web.csproj -c Release -o /app/publish -p:SkipClientBuild=true

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy published app from build stage
COPY --from=build /app/publish .

# Expose port
EXPOSE 5261

# Set environment
ENV ASPNETCORE_URLS=http://+:5261

# Run the app
ENTRYPOINT ["dotnet", "HstReceipts.Web.dll"]
