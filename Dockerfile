# Use the official .NET 8 runtime as the base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the .NET 8 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["DataverseMcp.FunctionApp.csproj", "./"]
RUN dotnet restore "DataverseMcp.FunctionApp.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "DataverseMcp.FunctionApp.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "DataverseMcp.FunctionApp.csproj" -c Release -o /app/publish

# Final stage - runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables for Render
ENV ASPNETCORE_URLS=http://*:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Create a startup script that will run the web server
ENTRYPOINT ["dotnet", "DataverseMcp.FunctionApp.dll"]