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

# ─── Usuário não-root ────────────────────────────────────────────────────────
# A imagem aspnet:8.0 já traz o usuário `app` (UID em $APP_UID = 1654, HOME
# /home/app), mas roda como root por padrão. Binários e wwwroot continuam do
# root e só-leitura para o processo: a aplicação não consegue alterar o próprio
# código. O único lugar em que ela grava dentro de /app é logs/ (sink de arquivo
# do Serilog, caminho relativo ao WORKDIR), então só essa pasta é do `app`.
# Demais escritas já caem em locais do usuário: chaves do Data Protection em
# $HOME/.aspnet e temporários em /tmp.
RUN mkdir -p /app/logs && chown "$APP_UID:$APP_UID" /app/logs

# Numérico (não "app") para que orquestradores com runAsNonRoot consigam
# verificar que não é root sem consultar /etc/passwd.
USER $APP_UID

# ─── Porta ───────────────────────────────────────────────────────────────────
# Sem ASPNETCORE_URLS de propósito: ele tem precedência sobre tudo e fazia a
# aplicação ignorar o PORT da Railway. Agora o Program.cs mapeia PORT para as
# portas HTTP do Kestrel; sem PORT, vale o 8080 abaixo (explícito aqui para não
# depender do default da imagem base). Porta > 1024: não exige privilégio.
ENV ASPNETCORE_HTTP_PORTS=8080
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
# ASPNETCORE_URLS/PORT/ASPNETCORE_HTTP_PORTS na mesma precedência do Kestrel
# (PortaHttpSetup), então acompanha a porta configurada.
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD ["dotnet", "Tratoo.API.dll", "--healthcheck"]

# Forma exec, sem shell intermediário: o `dotnet` é o PID 1 e recebe o SIGTERM
# da Railway/Docker diretamente. Não trocar por forma shell nem por script de
# entrada sem `exec` — o sinal pararia no shell e o encerramento gracioso some.
ENTRYPOINT ["dotnet", "Tratoo.API.dll"]
