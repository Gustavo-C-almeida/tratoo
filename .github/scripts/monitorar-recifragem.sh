#!/usr/bin/env bash
# Lê o /health/ready público de um ambiente e avalia a recifragem e a chave de dados.
# Sem segredos: só GET numa rota anônima. Uso: monitorar-recifragem.sh <nome> <url-base>
# Falha (exit 1) quando algo precisa de ação; avisa (::warning) quando há rotação em curso.
set -euo pipefail

NOME="$1"
BASE="${2%/}"
TENTATIVAS="${TENTATIVAS:-4}"   # várias leituras para passar pelas réplicas
falhou=0

for i in $(seq 1 "$TENTATIVAS"); do
  if ! corpo="$(curl -sS --max-time 20 "$BASE/health/ready")"; then
    echo "::error::$NOME: /health/ready não respondeu."
    exit 1
  fi
  if ! echo "$corpo" | jq -e '.checks' >/dev/null 2>&1; then
    echo "::error::$NOME: resposta inesperada do /health/ready."
    exit 1
  fi

  instancia="$(echo "$corpo" | jq -r '.instancia // "?"')"
  geral="$(echo "$corpo" | jq -r '.status')"
  dados_status="$(echo "$corpo" | jq -r '.checks[] | select(.nome=="dados-cifrados") | .status')"
  dados_desc="$(echo "$corpo" | jq -r '.checks[] | select(.nome=="dados-cifrados") | .descricao')"
  rec_status="$(echo "$corpo" | jq -r '.checks[] | select(.nome=="recifragem") | .status')"
  rec_desc="$(echo "$corpo" | jq -r '.checks[] | select(.nome=="recifragem") | .descricao')"

  echo "$NOME [$instancia] geral=$geral dados-cifrados=$dados_status recifragem=${rec_status:-ausente}"

  if [ "$geral" = "Unhealthy" ] || [ "$dados_status" = "Unhealthy" ]; then
    echo "::error::$NOME [$instancia]: dados-cifrados $dados_status — $dados_desc"
    falhou=1
  elif [ "$dados_status" = "Degraded" ]; then
    echo "::warning::$NOME [$instancia]: $dados_desc"
  fi

  if [ "$rec_status" = "Degraded" ] || [ "$rec_status" = "Unhealthy" ]; then
    echo "::error::$NOME [$instancia]: recifragem — $rec_desc"
    falhou=1
  elif [[ "$rec_desc" == *"pode ser removida"* ]]; then
    echo "::notice::$NOME [$instancia]: $rec_desc Confira o roteiro em Docs/ROTACAO-CHAVES.md antes de remover."
  fi

  [ "$i" -lt "$TENTATIVAS" ] && sleep 2
done

exit "$falhou"
