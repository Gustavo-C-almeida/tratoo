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

# HEALTHCHECK do Docker consulta SOMENTE /health/live — de propósito.
# Queda de PostgreSQL/pgvector não deve reiniciar o processo: reiniciar não
# conserta banco fora do ar e só agrava (perde cache em memória, derruba
# conexões válidas, entra em crash loop). Dependência externa é readiness, e
# quem avalia isso é a Railway via healthcheckPath=/health/ready (railway.toml).
#
# A sonda é o próprio binário (`--healthcheck`), não curl: a imagem
# mcr.microsoft.com/dotnet/aspnet:8.0 não traz curl nem wget, e instalá-los só
# para isto adicionaria pacotes e superfície de ataque ao runtime. A sonda lê
# ASPNETCORE_URLS/PORT, então acompanha a porta configurada.
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD ["dotnet", "Tratoo.API.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "Tratoo.API.dll"]
