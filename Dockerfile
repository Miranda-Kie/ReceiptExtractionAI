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

# The Tesseract .NET wrapper (InteropDotNet) does NOT use system library search paths — it looks
# specifically in an "x64" folder next to the app's own DLL (BaseDirectory + "x64/"), mimicking
# how the Windows NuGet package bundles native binaries. Install the real libs via apt, discover
# what actually got installed, and symlink into that exact app-relative location.
RUN apt-get update && apt-get install -y --no-install-recommends \
        tesseract-ocr \
        libleptonica-dev \
        libtesseract-dev \
        libtesseract5 \
    && LEPT_LIB=$(ldconfig -p | grep -i liblept | awk '{print $NF}' | head -1) \
    && TESS_LIB=$(ldconfig -p | grep -i libtesseract | awk '{print $NF}' | head -1) \
    && echo "Resolved leptonica library: ${LEPT_LIB:-NOT FOUND}" \
    && echo "Resolved tesseract library: ${TESS_LIB:-NOT FOUND}" \
    && mkdir -p /app/x64 \
    && if [ -n "$LEPT_LIB" ]; then ln -sf "$LEPT_LIB" /app/x64/libleptonica-1.82.0.so; fi \
    && if [ -n "$TESS_LIB" ]; then \
         ln -sf "$TESS_LIB" /app/x64/libtesseract53.so; \
         ln -sf "$TESS_LIB" /app/x64/libtesseract5.so; \
       fi \
    && rm -rf /var/lib/apt/lists/*

# Expose port
EXPOSE 5261

# Set environment
ENV ASPNETCORE_URLS=http://+:5261
# Disable config-file hot-reload watcher — its inotify usage exceeds Render's container limits
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

# Run the app
ENTRYPOINT ["dotnet", "HstReceipts.Web.dll"]
