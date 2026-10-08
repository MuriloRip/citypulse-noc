FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CityPulse.Api/CityPulse.Api.csproj CityPulse.Api/
RUN dotnet restore CityPulse.Api/CityPulse.Api.csproj
COPY CityPulse.Api/ CityPulse.Api/
RUN dotnet publish CityPulse.Api/CityPulse.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish/ ./
RUN mkdir -p /data && chown -R "$APP_UID:$APP_UID" /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__CityPulse="Data Source=/data/citypulse.db"
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CityPulse.Api.dll"]
