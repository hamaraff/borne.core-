FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY borne.core/borne.core.csproj borne.core/
COPY borne.business/borne.business.csproj borne.business/
COPY borne.infrastructure/borne.infrastructure.csproj borne.infrastructure/
COPY borne.central.api/borne.central.api.csproj borne.central.api/
RUN dotnet restore borne.central.api/borne.central.api.csproj
COPY . .
RUN dotnet publish borne.central.api/borne.central.api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "borne.central.api.dll"]
