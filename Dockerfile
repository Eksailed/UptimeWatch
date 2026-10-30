# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until a dependency changes.
COPY Directory.Build.props ./
COPY src/UptimeWatch.Domain/UptimeWatch.Domain.csproj src/UptimeWatch.Domain/
COPY src/UptimeWatch.Application/UptimeWatch.Application.csproj src/UptimeWatch.Application/
COPY src/UptimeWatch.Infrastructure/UptimeWatch.Infrastructure.csproj src/UptimeWatch.Infrastructure/
COPY src/UptimeWatch.Web/UptimeWatch.Web.csproj src/UptimeWatch.Web/
RUN dotnet restore src/UptimeWatch.Web/UptimeWatch.Web.csproj

COPY src/ src/
RUN dotnet publish src/UptimeWatch.Web/UptimeWatch.Web.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Directory for ASP.NET Data Protection keys (mount a volume here, see docker-compose.yml). Created and owned by
# the non-root user so a fresh named volume inherits the right owner.
RUN mkdir /keys && chown app:app /keys

COPY --from=build /app/publish .

# The base image also sets ASPNETCORE_HTTP_PORTS=8080; blank it so ASPNETCORE_URLS is the only source
# (otherwise Kestrel logs an "Overriding HTTP_PORTS" warning on every start).
ENV ASPNETCORE_HTTP_PORTS="" \
    ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/keys
EXPOSE 8080

# The base image ships the unprivileged user "app".
USER app
ENTRYPOINT ["dotnet", "UptimeWatch.Web.dll"]
