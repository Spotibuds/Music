FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Music.csproj", "."]
RUN dotnet restore "Music.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "Music.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Music.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Music.dll"] 