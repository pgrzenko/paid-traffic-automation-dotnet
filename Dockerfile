FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/PaidTraffic.Domain/*.csproj src/PaidTraffic.Domain/packages.lock.json src/PaidTraffic.Domain/
COPY src/PaidTraffic.Api/*.csproj src/PaidTraffic.Api/packages.lock.json src/PaidTraffic.Api/
RUN dotnet restore src/PaidTraffic.Api --locked-mode
COPY src/ src/
RUN dotnet publish src/PaidTraffic.Api -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PaidTraffic.Api.dll"]
