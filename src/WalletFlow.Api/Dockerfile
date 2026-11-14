FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "./"]
# Chaque projet arrive avec son fichier de verrouillage : la restauration refuse toute autre version des paquets (SS §10).
COPY ["src/WalletFlow.Api/WalletFlow.Api.csproj", "src/WalletFlow.Api/packages.lock.json", "src/WalletFlow.Api/"]
COPY ["src/WalletFlow.Application/WalletFlow.Application.csproj", "src/WalletFlow.Application/packages.lock.json", "src/WalletFlow.Application/"]
COPY ["src/WalletFlow.Domain/WalletFlow.Domain.csproj", "src/WalletFlow.Domain/packages.lock.json", "src/WalletFlow.Domain/"]
COPY ["src/WalletFlow.Infrastructure/WalletFlow.Infrastructure.csproj", "src/WalletFlow.Infrastructure/packages.lock.json", "src/WalletFlow.Infrastructure/"]
RUN dotnet restore "src/WalletFlow.Api/WalletFlow.Api.csproj" --locked-mode
COPY . .
WORKDIR "/src/src/WalletFlow.Api"
RUN dotnet build "./WalletFlow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WalletFlow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

# L'API compte les jours à l'heure de Paris : l'image finale doit contenir les fuseaux horaires (tzdata), sinon elle refuse
# de démarrer. À vérifier à l'étape de la mise en production, avec le reste de cette image.
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WalletFlow.Api.dll"]
