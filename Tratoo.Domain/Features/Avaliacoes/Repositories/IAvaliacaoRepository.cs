using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Avaliacoes
{
    public interface IAvaliacaoRepository
    {
        Task<Avaliacao?> GetByIdAsync(Guid id);

        /// <summary>Retorna a avaliação que o usuário deve preencher para um dado contrato.</summary>
        Task<Avaliacao?> GetPendenteDoUsuarioNoContratoAsync(Guid contratoServicoId, int avaliadorId);

        /// <summary>Retorna as duas avaliações geradas para o contrato (avaliação bilateral).</summary>
        Task<List<Avaliacao>> GetDoContratoAsync(Guid contratoServicoId);

        /// <summary>Lista avaliações Publicadas públicas de um avaliado, com paginação.</summary>
        Task<List<Avaliacao>> GetPublicasPorAvaliadoAsync(int avaliadoId, int pagina, int tamanhoPagina);

        /// <summary>Todos os slots pendentes (Nota == null) onde o usuário é avaliador.</summary>
        Task<List<Avaliacao>> GetPendentesPorAvaliadorAsync(int avaliadorId);

        /// <summary>Todas as avaliações onde o usuário é avaliador ou avaliado.</summary>
        Task<List<Avaliacao>> GetMinhasAsync(int userId);

        /// <summary>Avaliações Pendentes criadas antes de <paramref name="limite"/> (para expiração automática).</summary>
        Task<List<Avaliacao>> GetPendentesExpiradosAsync(DateTime limite);

        /// <summary>Avaliações pendentes sem nota criadas entre limiteInicio e limiteFim (D+3 reminder).</summary>
        Task<List<Avaliacao>> GetPendentesParaLembreteAsync(DateTime limiteInicio, DateTime limiteFim);

        Task<ReputacaoResumo?> GetReputacaoAsync(int userId);

        Task AddAsync(Avaliacao avaliacao);
        Task UpsertReputacaoAsync(ReputacaoResumo resumo);

        /// <summary>Conta quantos contratos o par de usuários já completou (anti-fraude básico).</summary>
        Task<int> CountAvaliacoesMutuasAsync(int userId1, int userId2);

        Task SaveChangesAsync();
    }
}
