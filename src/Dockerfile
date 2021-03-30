FROM mcr.microsoft.com/dotnet/aspnet:5.0.4 AS base
WORKDIR /app
COPY . .

FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /build
COPY . .

RUN dotnet restore "src/TimonMigrations.fsproj"
RUN dotnet restore "src/TimonServer.fsproj"

FROM build AS publish
RUN dotnet publish "src/TimonServer.fsproj" -c Release -o /app/publish

FROM build AS publish_migrate
RUN dotnet publish "src/TimonMigrations.fsproj" -c Release -o /app/publish

FROM base AS final_migrate
WORKDIR /app
COPY --from=publish_migrate /app/publish .

ENTRYPOINT ["dotnet", "TimonMigrations.dll"]

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN cp .MyCertificate.crt /usr/local/share/ca-certificates
RUN update-ca-certificates

RUN chmod 777 .MyCertificate.pfx

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS="http://*:5000"
EXPOSE 5000

RUN addgroup --gid 998 --system appgroup \
    && adduser --uid 1004 --system appuser --ingroup appgroup

USER appuser

ENTRYPOINT ["dotnet", "TimonServer.dll"]
