FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем оба csproj для кэширования nuget-пакетов
COPY ["src/Cafe.API/Cafe.API.csproj", "src/Cafe.API/"]
COPY ["src/Cafe.Core/Cafe.Core.csproj", "src/Cafe.Core/"]
RUN dotnet restore "src/Cafe.API/Cafe.API.csproj"

# Копируем весь исходный код
COPY . .
WORKDIR "/src/src/Cafe.API"
RUN dotnet build "Cafe.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Cafe.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Cafe.API.dll"]