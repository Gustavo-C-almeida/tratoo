#!/usr/bin/env bash
# Pacotes NuGet vulneráveis, diretos E transitivos, pela base de avisos do nuget.org.
# Uso: verificar-vulnerabilidades.sh <solução>   (precisa do restore feito antes)
#
# O `dotnet list package --vulnerable` sai com 0 mesmo achando algo, então a decisão é
# tomada aqui, sobre a saída JSON:
#   • High e Critical → erro, o job falha;
#   • Moderate e Low  → aviso (::warning), não bloqueia.
# Critério definido com a base limpa (08/11/2026: nenhum pacote vulnerável). Não há lista
# de exceções: se surgir um falso positivo, a exceção precisa ser criada aqui, com o
# GHSA e o motivo, nunca rebaixando o critério para todos os pacotes.
set -euo pipefail

SOLUCAO="${1:?uso: $0 <solução>}"

if ! json="$(dotnet list "$SOLUCAO" package --vulnerable --include-transitive --format json)"; then
  echo "::error::dotnet list package --vulnerable falhou (sem restore ou sem acesso ao nuget.org)."
  exit 1
fi

# Uma linha por (projeto, pacote, aviso): severidade<TAB>projeto<TAB>pacote<TAB>versão<TAB>tipo<TAB>url
achados="$(echo "$json" | jq -r '
  .projects[] | (.path | split("/") | last) as $proj
  | (.frameworks // [])[]
  | ((.topLevelPackages // [])[] | . + {tipo: "direto"}),
    ((.transitivePackages // [])[] | . + {tipo: "transitivo"})
  | . as $p | (.vulnerabilities // [])[]
  | [.severity, $proj, $p.id, $p.resolvedVersion, $p.tipo, .advisoryurl] | @tsv')"

if [ -z "$achados" ]; then
  echo "Nenhum pacote vulnerável (diretos e transitivos)."
  exit 0
fi

bloqueia=0
while IFS=$'\t' read -r sev proj pacote versao tipo url; do
  msg="$pacote $versao ($tipo, $proj): $sev — $url"
  case "$sev" in
    High|Critical) echo "::error::$msg"; bloqueia=1 ;;
    *)             echo "::warning::$msg" ;;
  esac
done <<< "$(echo "$achados" | sort -u)"

if [ "$bloqueia" = 1 ]; then
  echo "Há pacote com vulnerabilidade High/Critical. Atualize-o (ou o pacote que o traz) e regenere os lock files."
fi
exit "$bloqueia"
