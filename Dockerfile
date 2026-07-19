# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy project files
COPY src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj ./Kioku.Mcp.Server/

# Restore dependencies
RUN dotnet restore ./Kioku.Mcp.Server/Kioku.Mcp.Server.csproj

# Copy source code
COPY src/Kioku.Mcp.Server/ ./Kioku.Mcp.Server/

# Build and publish
RUN dotnet publish ./Kioku.Mcp.Server/Kioku.Mcp.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Install curl for health checks
RUN apk add --no-cache curl

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5173
ENV KIOKU_HTTP_HOST=0.0.0.0
ENV KIOKU_HTTP_PORT=5173
ENV KIOKU_TRANSPORT=http

# Expose port
EXPOSE 5173

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5173/health/live || exit 1

# Entry point
ENTRYPOINT ["dotnet", "Kioku.Mcp.Server.dll"]
