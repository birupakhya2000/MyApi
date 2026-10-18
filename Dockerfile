# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so this layer is cached until the project file changes
COPY Directory.Build.props ./
COPY src/MyApi.Api/MyApi.Api.csproj src/MyApi.Api/
RUN dotnet restore src/MyApi.Api/MyApi.Api.csproj

COPY .editorconfig ./
COPY src/ src/
RUN dotnet publish src/MyApi.Api/MyApi.Api.csproj -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .

# Run as the non-root user built into the .NET images
USER $APP_UID
ENTRYPOINT ["dotnet", "MyApi.Api.dll"]