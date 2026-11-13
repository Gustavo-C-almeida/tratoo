using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public interface IContratoServicoRepository
    {
        Task<ContratoServico?> GetByIdAsync(Guid id);
        Task<ContratoServico?> GetByPropostaIdAsync(Guid propostaId);
        Task<List<ContratoServico>> GetDoContratanteAsync(int contratanteId);
        Task<List<ContratoServico>> GetDoPrestadorAsync(int prestadorId);
        Task<List<ContratoServico>> GetExpiradosAsync(DateTime agora);

        /// <summary>
        /// Cancela o contrato por expiração (e recusa a proposta / reabre o projeto) só se
        /// ele ainda estiver pendente e vencido no momento da escrita. false = outro
        /// processo ou o usuário já mudou o contrato; nada foi alterado.
        /// </summary>
        Task<bool> CancelarPorExpiracaoAsync(
            Guid contratoId, Guid propostaId, int projetoId, DateTime agora,
            string motivoContrato, string motivoProposta);
        // ── Integridade das transições de estado (achado D3) ──────────────────
        // Cada transição é um UPDATE condicional: só muda se o contrato ainda estiver no estado
        // de origem. Com duas requisições (ou duas réplicas) ao mesmo tempo, o Postgres
        // serializa pelo lock da linha e só uma delas vê 1 linha afetada.

        /// <summary>Transação no contexto compartilhado da requisição (vale para todos os repositórios).</summary>
        Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> IniciarTransacaoAsync();

        /// <summary>SELECT ... FOR UPDATE na linha do contrato (exige transação aberta).</summary>
        Task BloquearParaAtualizacaoAsync(Guid contratoId);

        /// <summary>Status atual, lido do banco (sem rastreamento).</summary>
        Task<ContratoServicoStatus?> ObterStatusAsync(Guid contratoId);

        /// <summary>Ativo → AguardandoAprovacaoEntrega (marca EntregaRegistradaEm). True se mudou.</summary>
        Task<bool> TentarRegistrarEntregaAsync(Guid contratoId, DateTime agora);

        /// <summary>AguardandoAprovacaoEntrega → Encerrado. True se mudou.</summary>
        Task<bool> TentarAprovarEntregaAsync(Guid contratoId);

        /// <summary>AguardandoAprovacaoEntrega → Ativo (limpa EntregaRegistradaEm). True se mudou.</summary>
        Task<bool> TentarRejeitarEntregaAsync(Guid contratoId);

        Task AddAsync(ContratoServico contrato);
        Task AddSnapshotAsync(ContratoSnapshot snapshot);
        Task AddHistoricoAsync(HistoricoAssinatura historico);
        Task SaveChangesAsync();
    }
}
