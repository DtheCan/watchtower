# ===== Этап 1: Сборка =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

LABEL org.opencontainers.image.source=https://github.com/DtheCan/watchtower

COPY w2.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ===== Этап 2: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

ENTRYPOINT ["dotnet", "w2.dll"]