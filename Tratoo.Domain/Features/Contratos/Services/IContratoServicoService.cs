using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public interface IContratoServicoService
    {
        Task<ContratoServico> GerarAsync(PropostaProjeto proposta, PropostaVersao versao);
        Task AssinarAsync(Guid contratoId, int usuarioId, string ip);
        Task<ContratoDetalheDto?> ObterDetalheAsync(Guid contratoId, int usuarioId);
        Task<List<ContratoResumoDto>> ListarDoContratanteAsync(int contratanteId);
        Task<List<ContratoResumoDto>> ListarDoPrestadorAsync(int prestadorId);
        Task<string> ObterUrlPdfAsync(Guid contratoId, int usuarioId);
        Task RegistrarEntregaAsync(Guid contratoId, int prestadorId);
        Task CancelarAsync(Guid contratoId, int usuarioId, string? motivo);
        Task ExpirarContratosAsync();
    }
}
