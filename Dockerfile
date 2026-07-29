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
# how the Windows NuGet package bundles leptonica-1.82.0.dll / tesseract50.dll (confirmed by
# inspecting the local Windows build output). Install the real libs via apt, then locate them
# using dpkg -L (package file listing — not dependent on ldconfig cache timing) and symlink into
# that exact app-relative location. Fails the build loudly with full diagnostics if not found,
# rather than deferring an unclear failure to runtime.
RUN apt-get update && apt-get install -y --no-install-recommends \
        tesseract-ocr \
        libleptonica-dev \
        libtesseract-dev \
        libtesseract5 \
    && echo "=== libleptonica-dev contents ===" && dpkg -L libleptonica-dev | grep '\.so' \
    && echo "=== libtesseract5 contents ===" && dpkg -L libtesseract5 | grep '\.so' \
    && echo "=== libtesseract-dev contents ===" && dpkg -L libtesseract-dev | grep '\.so' \
    && LEPT_LIB=$(dpkg -L libleptonica-dev libleptonica5 2>/dev/null | grep -E '\.so(\.[0-9]+)*$' | grep -i lept | head -1) \
    && TESS_LIB=$(dpkg -L libtesseract5 libtesseract-dev 2>/dev/null | grep -E '\.so(\.[0-9]+)*$' | grep -i tesseract | head -1) \
    && if [ -z "$LEPT_LIB" ]; then LEPT_LIB=$(find / -xdev -iname 'liblept*.so*' 2>/dev/null | head -1); fi \
    && if [ -z "$TESS_LIB" ]; then TESS_LIB=$(find / -xdev -iname 'libtesseract*.so*' 2>/dev/null | head -1); fi \
    && echo "Resolved leptonica library: ${LEPT_LIB:-NOT FOUND}" \
    && echo "Resolved tesseract library: ${TESS_LIB:-NOT FOUND}" \
    && mkdir -p /app/x64 \
    && if [ -z "$LEPT_LIB" ] || [ -z "$TESS_LIB" ]; then \
         echo "FATAL: could not locate native OCR libraries after install." && exit 1; \
       fi \
    && ln -sf "$LEPT_LIB" /app/x64/libleptonica-1.82.0.so \
    && ln -sf "$TESS_LIB" /app/x64/libtesseract50.so \
    && ls -la /app/x64/ \
    && rm -rf /var/lib/apt/lists/*

# Expose port
EXPOSE 5261

# Set environment
ENV ASPNETCORE_URLS=http://+:5261
# Disable config-file hot-reload watcher — its inotify usage exceeds Render's container limits
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

# Run the app
ENTRYPOINT ["dotnet", "HstReceipts.Web.dll"]
