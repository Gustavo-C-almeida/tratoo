-- D3 — inconsistências do fluxo de entrega em produção. SOMENTE LEITURA, SÓ CONTAGENS.
-- Nenhum identificador, nome, e-mail, valor ou texto é devolvido: uma única linha de números.
BEGIN TRANSACTION READ ONLY;
WITH pendentes AS (
    SELECT "ContratoServicoId" AS contrato, count(*) AS qtd
    FROM "Entregas" WHERE "Status" = 'PendenteAprovacao'
    GROUP BY "ContratoServicoId"
),
ultima_entrega AS (
    SELECT DISTINCT ON ("ContratoServicoId") "ContratoServicoId" AS contrato, "Status" AS status
    FROM "Entregas"
    ORDER BY "ContratoServicoId", "CriadoEm" DESC
)
SELECT
    -- (1) Mais de uma entrega pendente no mesmo contrato (registro concorrente).
    (SELECT count(*) FROM pendentes WHERE qtd > 1)                                         AS contratos_com_entregas_pendentes_duplicadas,
    -- (2) Contrato Ativo com entrega pendente: travado (não aceita nova entrega, nem aprovar/rejeitar).
    (SELECT count(*) FROM "ContratosServico" c JOIN pendentes p ON p.contrato = c."Id"
      WHERE c."Status" = 'Ativo')                                                          AS contratos_ativos_travados_por_entrega_pendente,
    -- (3) Contrato reaberto (Ativo, última entrega Rejeitada) com o pagamento já saindo ou liberado
    --     (aprovar × rejeitar simultâneos).
    (SELECT count(*) FROM "ContratosServico" c
       JOIN ultima_entrega u ON u.contrato = c."Id"
       JOIN "Pagamentos" pg ON pg."ContratoServicoId" = c."Id"
      WHERE c."Status" = 'Ativo' AND u.status = 'Rejeitada'
        AND pg."Status" IN ('TransferenciaEmProgresso', 'Liberado', 'FalhaTransferencia'))  AS contratos_reabertos_com_pagamento_liberado,
    -- (4) Contrato Cancelado com entrega pendente (registro × cancelamento simultâneos).
    (SELECT count(*) FROM "ContratosServico" c JOIN pendentes p ON p.contrato = c."Id"
      WHERE c."Status" = 'Cancelado')                                                      AS contratos_cancelados_com_entrega_pendente,
    -- (5) Contrato AguardandoAprovacaoEntrega com pagamento já estornado (variante de (4)).
    (SELECT count(*) FROM "ContratosServico" c JOIN "Pagamentos" pg ON pg."ContratoServicoId" = c."Id"
      WHERE c."Status" = 'AguardandoAprovacaoEntrega' AND pg."Status" = 'Estornado')        AS contratos_aguardando_aprovacao_com_pagamento_estornado,
    -- (6) Informativo: entrega aprovada, contrato encerrado e pagamento ainda Retido
    --     (liberação imediata falhou e ainda não foi retomada).
    (SELECT count(*) FROM "ContratosServico" c
       JOIN ultima_entrega u ON u.contrato = c."Id"
       JOIN "Pagamentos" pg ON pg."ContratoServicoId" = c."Id"
      WHERE c."Status" = 'Encerrado' AND u.status = 'Aprovada' AND pg."Status" = 'Retido')  AS encerrados_aprovados_com_pagamento_retido;
ROLLBACK;
