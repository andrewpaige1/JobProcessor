# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy and restore project
COPY EmployeeService.csproj ./
RUN dotnet restore

# Copy the rest of the app and publish
COPY . ./
RUN dotnet publish -c Release -o out

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy the build output
COPY --from=build /app/out ./

# Expose the gRPC port
EXPOSE 8080

# Run the app
ENTRYPOINT ["dotnet", "EmployeeService.dll"]
