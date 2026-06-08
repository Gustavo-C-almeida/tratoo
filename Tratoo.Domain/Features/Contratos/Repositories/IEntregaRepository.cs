using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public interface IEntregaRepository
    {
        Task AddAsync(Entrega entrega);

        /// <summary>Entrega mais recente do contrato, com Anexos e Links carregados.</summary>
        Task<Entrega?> GetAtualPorContratoAsync(Guid contratoServicoId);

        /// <summary>Retorna a entrega ativa (PendenteAprovacao) do contrato, se houver.</summary>
        Task<Entrega?> GetPendentePorContratoAsync(Guid contratoServicoId);

        Task<EntregaAnexo?> GetAnexoAsync(Guid anexoId);

        Task AddHistoricoAsync(HistoricoContrato historico);

        Task<List<HistoricoContrato>> GetHistoricoPorContratoAsync(Guid contratoServicoId);

        Task SaveChangesAsync();
    }
}
