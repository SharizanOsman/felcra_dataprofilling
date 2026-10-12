# Builds the API together with both Blazor WebAssembly clients (served under /admin and /field).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package downloads are cached between source changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Felcra.Profiling.Localization/*.csproj src/Felcra.Profiling.Localization/
COPY src/Felcra.Profiling.Domain/*.csproj src/Felcra.Profiling.Domain/
COPY src/Felcra.Profiling.Contracts/*.csproj src/Felcra.Profiling.Contracts/
COPY src/Felcra.Profiling.Application/*.csproj src/Felcra.Profiling.Application/
COPY src/Felcra.Profiling.Infrastructure/*.csproj src/Felcra.Profiling.Infrastructure/
COPY src/Felcra.Profiling.Analytics/*.csproj src/Felcra.Profiling.Analytics/
COPY src/Felcra.Profiling.UI.Shared/*.csproj src/Felcra.Profiling.UI.Shared/
COPY src/Felcra.Profiling.Web.Admin/*.csproj src/Felcra.Profiling.Web.Admin/
COPY src/Felcra.Profiling.Web.Field/*.csproj src/Felcra.Profiling.Web.Field/
COPY src/Felcra.Profiling.Api/*.csproj src/Felcra.Profiling.Api/
RUN dotnet restore src/Felcra.Profiling.Api/Felcra.Profiling.Api.csproj

COPY src/ src/
RUN dotnet publish src/Felcra.Profiling.Api/Felcra.Profiling.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Photos, logs and data-protection keys live here; mount a volume (see deploy/).
RUN mkdir -p /app/data && chown $APP_UID /app/data
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 CMD ["dotnet", "Felcra.Profiling.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "Felcra.Profiling.Api.dll"]
