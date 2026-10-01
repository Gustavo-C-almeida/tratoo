#!/usr/bin/env bash
# Gera o certificado TLS autoassinado do Nginx local (docker-compose).
#
#   bash infra/nginx/gerar-certificado.sh           # cria se não existir
#   bash infra/nginx/gerar-certificado.sh --force   # recria
#
# Requer OpenSSL >= 1.1.1 (vem com o Git Bash no Windows). Os arquivos ficam em
# infra/nginx/certs/ e NUNCA vão para o git (.gitignore: *.crt, *.key).
set -euo pipefail

# No Git Bash (MSYS), argumentos começando com "/" viram caminhos do Windows
# ("/CN=localhost" → "C:/Program Files/Git/CN=localhost"). "//" escapa só o -subj;
# desligar a conversão inteira (MSYS_NO_PATHCONV) quebraria os caminhos dos arquivos
# passados ao openssl.exe nativo.
case "$(uname -s)" in
    MINGW*|MSYS*) SUBJ="//CN=localhost" ;;
    *)            SUBJ="/CN=localhost" ;;
esac

DIR="$(cd "$(dirname "$0")" && pwd)/certs"
CRT="$DIR/localhost.crt"
KEY="$DIR/localhost.key"

mkdir -p "$DIR"

if [[ -f "$CRT" && -f "$KEY" && "${1:-}" != "--force" ]]; then
    echo "Certificado já existe em $DIR (use --force para recriar)."
    exit 0
fi

openssl req -x509 -newkey rsa:2048 -sha256 -days 825 -nodes \
    -keyout "$KEY" -out "$CRT" \
    -subj "$SUBJ" \
    -addext "subjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1" \
    -addext "extendedKeyUsage=serverAuth"

echo "Certificado gerado em $DIR (válido por 825 dias, autoassinado)."
