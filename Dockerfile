FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY backend/LojaVirtual.csproj backend/

RUN dotnet restore backend/LojaVirtual.csproj

COPY backend/ backend/

RUN dotnet publish backend/LojaVirtual.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "LojaVirtual.dll"]
