FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet publish borne.device.api/borne.device.api.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080 \
    BORNE_PORT=8080 \
    BORNE_DATA_PATH=/var/lib/borne/borne.db
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /var/lib/borne && chown -R $APP_UID:$APP_UID /app /var/lib/borne
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "borne.device.api.dll"]
