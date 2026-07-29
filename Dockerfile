# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10 AS build
WORKDIR /app

# Copy project files
COPY . .

# Build the project
RUN dotnet build src/HstReceipts.Web/HstReceipts.Web.csproj -c Release

# Publish the project
RUN dotnet publish src/HstReceipts.Web/HstReceipts.Web.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10
WORKDIR /app

# Copy published app from build stage
COPY --from=build /app/publish .

# Expose port
EXPOSE 5261

# Set environment
ENV ASPNETCORE_URLS=http://+:5261

# Run the app
ENTRYPOINT ["dotnet", "HstReceipts.Web.dll"]
