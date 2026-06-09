FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Kopiuje plik projektu i przywraca zależności
COPY ["LegalLawBot_Csharp.csproj", "./"]
RUN dotnet restore "./LegalLawBot_Csharp.csproj"

# Kopiuje całą resztę kodu i buduje aplikację
COPY . .
RUN dotnet build "LegalLawBot_Csharp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "LegalLawBot_Csharp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Tworzy końcowy obraz produkcyjny
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LegalLawBot_Csharp.dll"]