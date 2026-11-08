# Conciliação de transferências PIX com resultado incerto: plano

> **Status: planejamento, não implementado.** Este documento define o que a conciliação
> automática precisa fazer e as decisões em aberto. O comportamento atual (sem conciliação) está
> em [ARQUITETURA.md §6.4.1](ARQUITETURA.md#641-resultado-incerto-na-transferência-pix).
> Fontes do Asaas consultadas em 08/11/2026 em docs.asaas.com.

## 1. Problema

Desde a correção do achado 08, uma chamada a `POST /v3/transfers` sem resposta conclusiva não é
repetida automaticamente. Isso acontece em timeout, falha de rede, 5xx, 2xx sem `id` ou status não
documentado. O pagamento fica em `TransferenciaEmProgresso` com `StatusGateway = RESULTADO_INCERTO`.
O risco de pagar duas vezes caiu, mas surgiu outro risco, operacional: **pagamentos parados**,
em que o prestador não recebe até alguém conciliar à mão.

Há também o caso sem marca: o processo cai entre a resposta 2xx do Asaas e o `SaveChanges`. O
pagamento fica em `TransferenciaEmProgresso` com `AsaasTransferenciaId IS NULL`.

**Candidatos à conciliação:**
`Status = TransferenciaEmProgresso AND (StatusGateway = 'RESULTADO_INCERTO' OR AsaasTransferenciaId IS NULL)`.

## 2. O que a documentação do Asaas permite

| Recurso | O que a documentação diz | Uso na conciliação |
|---|---|---|
| `externalReference` na criação | Aceito em `POST /v3/transfers` ("Identificador da transferência no seu sistema") | Desde o commit `3870065`, vai com o `Pagamento.Id`. Transferências **anteriores** a ele não têm |
| `GET /v3/transfers` | Lista paginada. Filtros: `dateCreated[ge/le]`, `transferDate[ge/le]`, `type`. **Não filtra por `externalReference`** | Buscar pela janela de datas e filtrar `externalReference` do nosso lado |
| `GET /v3/transfers/{id}` | "Recuperar uma única transferência" | Atualizar o status depois de vincular o `id` |
| Webhooks `TRANSFER_*` | `CREATED`, `PENDING`, `IN_BANK_PROCESSING`, `BLOCKED`, `DONE`, `FAILED`, `CANCELLED`, sem sequência obrigatória. O payload traz o objeto `transfer`, e os campos seguem a resposta de "Recuperar uma única transferência" (que inclui `externalReference`) | Vincular o `id` assim que um evento chegar |
| Idempotência | **Não há** cabeçalho de idempotência documentado. O Asaas recomenda que o controle fique na integração | O controle fica no Tratoo (reivindicação atômica + resultado incerto) |

**A confirmar no sandbox antes de implementar:** se `transfer.externalReference` vem de fato nos
payloads de webhook. A documentação indica isso pelo schema, mas os exemplos de transferência não
mostram o campo.

## 3. Fontes de evidência, em ordem

1. **Webhooks já recebidos e não casados.** O webhook de transferência que não encontra o pagamento
   é gravado em `WebhookLogs` (com `PayloadJson` e `ErroMensagem = "Pagamento não encontrado no
   sistema"`) e marcado como processado. Para um pagamento incerto, o `TRANSFER_DONE` cai
   exatamente aí. Casar por `transfer.externalReference = Pagamento.Id` não custa chamada nenhuma.
2. **Fallback no próprio webhook (prevenção).** Quando a busca por `AsaasTransferenciaId` falha num
   evento `TRANSFER_*`, procurar pelo `externalReference`. Só vincular se o pagamento estiver em
   `TransferenciaEmProgresso`, sem `AsaasTransferenciaId` e com o mesmo valor. Isso resolve a
   maioria dos casos sozinho, no momento em que o Asaas avisa.
3. **Consulta ativa à API.** `GET /v3/transfers` com `dateCreated` cobrindo o dia da tentativa e o
   seguinte (o campo é data, sem hora), paginando até o fim, e filtrando `externalReference` do
   nosso lado.
4. **Sem `externalReference`** (transferências anteriores a `3870065`): casar por valor + chave PIX
   + data é fraco. Fica **manual**, sem automação.

## 4. Decisão por resultado

| Encontrado no Asaas | Ação | Estado final |
|---|---|---|
| 1 transferência, `DONE` | Vincular o `id`, `FinalizarLiberacaoAsync` (ledger negativo, avaliações, e-mail) | `Liberado` |
| 1 transferência, `PENDING` / `IN_BANK_PROCESSING` / `BLOCKED` | Vincular o `id`, limpar `RESULTADO_INCERTO` | `TransferenciaEmProgresso` (aguarda webhook) |
| 1 transferência, `FAILED` / `CANCELLED` | Vincular o `id` | `FalhaTransferencia` (o reprocessamento admin passa a ser seguro, porque a anterior é terminal) |
| 2 ou mais com o mesmo `externalReference` | **Não mexer.** Alerta: duplicidade real | Intervenção manual |
| Nenhuma, dentro da janela segura | Manter e voltar a consultar | `TransferenciaEmProgresso` |
| Nenhuma, depois da janela segura | Voltar para `Retido`, com a entrada `[CONCILIADO – NÃO ENCONTRADA]` no ledger | `Retido` (a próxima varredura tenta de novo) |

Toda transição é um `UPDATE … WHERE Id = @id AND Status = 'TransferenciaEmProgresso' AND
AsaasTransferenciaId IS NULL`, com a mesma disciplina da reivindicação atômica. Assim um webhook
que chega no meio da conciliação não é sobrescrito.

## 5. Desenho do job

- `PagamentoConciliacaoService`, sob `ExecucaoExclusiva` com uma chave nova (`…_0004`). Ele lê a
  API em lote, então rodar em dobro custa chamadas.
- Intervalo curto (ex.: 30 min), porque o custo de um pagamento parado é o prestador sem receber.
- Ordem: (1) WebhookLogs, (2) API, (3) decisão da tabela acima.
- Observabilidade: log em Information por pagamento conciliado (sem chave PIX nem dado pessoal) e
  um aviso em Error para todo candidato **mais velho que X horas**. Isso vale mesmo antes do job:
  é o alarme para a conciliação manual.

## 6. Testes necessários

- Cada linha da tabela do §4, com Postgres real e um fake da listagem do Asaas, incluindo paginação.
- Webhook `TRANSFER_DONE` sem `AsaasTransferenciaId` casado pelo `externalReference`, chegando antes
  e depois da conciliação ativa, com um único `Liberado` e uma única entrada negativa no ledger.
- Duas réplicas conciliando ao mesmo tempo, com a trava e a escrita condicional.
- Valor divergente com o mesmo `externalReference`: não vincula, só alerta.

## 7. Decisões em aberto

1. **Janela segura** antes de concluir "não foi criada": quanto tempo sem webhook e sem a
   transferência aparecer na listagem?
2. **Retorno automático para `Retido`** depois da janela, ou só com aprovação de um admin?
3. Habilitar o evento **`TRANSFER_CREATED`** no painel do Asaas, se ele ainda não estiver no
   webhook configurado. Ele chega logo depois da criação e fecha a maior parte dos casos.
4. **Webhook de validação de saque** (§8).

## 8. Webhook de validação de saque: o que a documentação diz e o que falta provar

Fonte: "Mecanismo para validação de saque via webhooks" (atualizada em 27/10/2026).

**Como funciona, segundo a documentação.**
- Depois de solicitada a operação, "aproximadamente cinco segundos depois" o Asaas envia `POST` à
  URL configurada, com `type = TRANSFER` e o objeto `transfer` (com `id`, `value`, `status` etc.).
- A aplicação responde `{"status":"APPROVED"}` ou `{"status":"REFUSED","refuseReason":"..."}`.
- **Fail-closed:** se a chamada falhar três vezes ou não vier status válido, a operação é cancelada.
- Ao ativar, **todas** as transferências e saques feitos pela API passam pela validação, inclusive
  nas subcontas (a configuração da conta raiz se aplica a elas). Operações pela interface web são
  opcionais.
- A documentação recomenda registrar a operação antes da validação e "utilizar o ID para evitar
  aprovações duplicadas".

**O que ela permitiria.**
- **Barrar duplicata no próprio Asaas:** aprovar só se existir um pagamento em
  `TransferenciaEmProgresso` para aquele `externalReference` sem outra transferência já aprovada,
  e recusar o resto.
- **Desfazer a incerteza na origem:** a chamada de validação traz o `transfer.id` cerca de 5 s
  depois da criação, mesmo que a nossa resposta tenha se perdido. Daria para vincular o `id`
  ali mesmo.

**O que não está provado, e impede implementar agora.**
- O exemplo de payload de `TRANSFER` **não mostra `externalReference`** (os de estorno Pix e split
  mostram). Sem esse campo, não há como ligar a validação ao pagamento com segurança.
- Não está documentado se uma transferência cancelada pela validação gera `TRANSFER_CANCELLED`.
  Para split, a documentação diz explicitamente que **não** gera.
- O prazo que o Asaas espera pela nossa resposta não está documentado.
- Por ser fail-closed e valer para a conta inteira, uma indisponibilidade do Tratoo (ou do Redis /
  Neon que a validação consultar) **cancela todos os saques**, inclusive operações que não são
  liberações de contrato.

**Próximo passo proposto:** um teste no **sandbox** com uma URL de validação que só registra o
payload, para responder às três primeiras perguntas. Só depois disso decidir se vale implementar.
