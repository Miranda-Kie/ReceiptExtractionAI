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

# The Tesseract .NET wrapper expects native library filenames matching its Windows-bundled DLLs
# (e.g. libleptonica-1.82.0.so, mirroring leptonica-1.82.0.dll) which Debian's package names
# don't match. Install the real libraries via apt, then symlink whatever actually got installed
# to the exact filenames the interop loader looks for, discovered at build time rather than guessed.
RUN apt-get update && apt-get install -y --no-install-recommends \
        tesseract-ocr \
        libleptonica-dev \
        libtesseract-dev \
        libtesseract5 \
    && LEPT_LIB=$(ldconfig -p | grep -i liblept | awk '{print $NF}' | head -1) \
    && TESS_LIB=$(ldconfig -p | grep -i libtesseract | awk '{print $NF}' | head -1) \
    && echo "Resolved leptonica library: ${LEPT_LIB:-NOT FOUND}" \
    && echo "Resolved tesseract library: ${TESS_LIB:-NOT FOUND}" \
    && LIBDIR=/usr/lib/x86_64-linux-gnu \
    && if [ -n "$LEPT_LIB" ]; then ln -sf "$LEPT_LIB" "$LIBDIR/libleptonica-1.82.0.so"; fi \
    && if [ -n "$TESS_LIB" ]; then \
         ln -sf "$TESS_LIB" "$LIBDIR/libtesseract53.so"; \
         ln -sf "$TESS_LIB" "$LIBDIR/libtesseract5.so"; \
       fi \
    && ldconfig \
    && rm -rf /var/lib/apt/lists/*

# Copy published app from build stage
COPY --from=build /app/publish .

# Expose port
EXPOSE 5261

# Set environment
ENV ASPNETCORE_URLS=http://+:5261
# Disable config-file hot-reload watcher — its inotify usage exceeds Render's container limits
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

# Run the app
ENTRYPOINT ["dotnet", "HstReceipts.Web.dll"]
