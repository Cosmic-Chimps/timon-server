FROM mcr.microsoft.com/dotnet/aspnet:5.0.4 AS base
# WORKDIR /app
# COPY . .

## Run Migrations
FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build_migrations
WORKDIR /build
COPY . .

RUN dotnet restore "TimonMigrations/TimonMigrations.fsproj"

FROM build_migrations AS publish_migrate
RUN dotnet publish "TimonMigrations/TimonMigrations.fsproj" -c Release -o /app/publish

FROM base AS final_migrate
WORKDIR /app/migrations
COPY --from=publish_migrate /app/publish .

RUN dotnet TimonMigrations.dll

## End Migrations

# Run Server

FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /build
COPY . .
RUN dotnet restore "TimonServer/TimonServer.fsproj"

FROM build AS publish
RUN dotnet publish "TimonServer/TimonServer.fsproj" -c Release -o /app/publish


FROM final_migrate AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN cp timon-identity-server.crt /usr/local/share/ca-certificates
RUN update-ca-certificates

RUN chmod 777 timon-identity-server.pfx
RUN chmod +x run.sh

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS="http://*:8080"
EXPOSE 8080

# RUN addgroup --gid 998 --system appgroup \
#     && adduser --uid 1004 --system appuser --ingroup appgroup

# USER appuser

ENTRYPOINT ["./run.sh"]
