# Build: compila e publica o Tratoo.API (referencia Tratoo.Domain)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Tratoo.Domain/Tratoo.Domain.csproj Tratoo.Domain/
COPY Tratoo.API/Tratoo.API.csproj Tratoo.API/
RUN dotnet restore Tratoo.API/Tratoo.API.csproj

COPY Tratoo.Domain/ Tratoo.Domain/
COPY Tratoo.API/ Tratoo.API/
RUN dotnet publish Tratoo.API/Tratoo.API.csproj -c Release -o /app/publish --no-restore

# Runtime: imagem final enxuta
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Tratoo.API serve o frontend estático via caminho relativo "../Tratoo.Web/wwwroot"
# (ver Program.cs) — por isso o wwwroot precisa ficar como irmão de /app dentro da imagem.
COPY Tratoo.Web/wwwroot /Tratoo.Web/wwwroot

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Tratoo.API.dll"]
