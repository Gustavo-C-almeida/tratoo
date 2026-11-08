#!/usr/bin/env bash
# Atalho para mudança SÓ de documentação, usado por todos os jobs obrigatórios do ci.yml.
# Escreve so_docs=true|false em $GITHUB_OUTPUT.
#
# Conservador: só vale se TODOS os arquivos alterados estiverem em Docs/ ou forem .md na
# raiz; qualquer outro arquivo, ou não conseguir calcular a diferença (branch nova,
# histórico ausente), dá so_docs=false e roda a CI completa. Precisa do checkout com
# fetch-depth: 0. Entradas por variável de ambiente, nunca interpoladas no script:
#   EVENTO      github.event_name
#   BASE_PR     github.event.pull_request.base.sha
#   ANTES_PUSH  github.event.before
set -euo pipefail

if [ "${EVENTO:-}" = "pull_request" ]; then base="${BASE_PR:-}"; else base="${ANTES_PUSH:-}"; fi
so_docs=false
if [ -n "$base" ] && [ "$base" != "0000000000000000000000000000000000000000" ] \
   && arquivos="$(git diff --name-only "$base" HEAD)" && [ -n "$arquivos" ]; then
  echo "Arquivos alterados:"; echo "$arquivos" | sed 's/^/  /'
  if ! echo "$arquivos" | grep -qvE '^(Docs/.+|[^/]+\.md)$'; then so_docs=true; fi
else
  echo "Não foi possível calcular os arquivos alterados: CI completa."
fi
echo "so_docs=$so_docs" >> "$GITHUB_OUTPUT"
if [ "$so_docs" = true ]; then
  echo "::notice::Só documentação (Docs/ ou .md na raiz): build, testes e imagem dispensados."
fi
