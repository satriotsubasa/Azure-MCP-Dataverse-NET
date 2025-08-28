# Use the official .NET 8 runtime as the base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the .NET 8 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["DataverseMcp.WebApi.csproj", "./"]
RUN dotnet restore "DataverseMcp.WebApi.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "DataverseMcp.WebApi.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "DataverseMcp.WebApi.csproj" -c Release -o /app/publish

# Final stage - runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables for Render.com
ENV ASPNETCORE_URLS=http://*:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080

# Run the web application
ENTRYPOINT ["dotnet", "DataverseMcp.WebApi.dll"]