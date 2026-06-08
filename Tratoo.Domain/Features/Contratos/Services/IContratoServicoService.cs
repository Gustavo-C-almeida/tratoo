using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public interface IContratoServicoService
    {
        Task<ContratoServico> GerarAsync(PropostaProjeto proposta, PropostaVersao versao);

        /// <summary>
        /// Gera e envia um OTP de 6 dígitos para o e-mail do usuário.
        /// Deve ser chamado antes de AssinarAsync. O OTP expira em 10 minutos.
        /// </summary>
        Task SolicitarOtpAssinaturaAsync(Guid contratoId, int usuarioId, string ip, string? userAgent);

        /// <summary>
        /// Valida o OTP e, se correto, registra a assinatura digital.
        /// Requer chamada prévia a SolicitarOtpAssinaturaAsync.
        /// </summary>
        Task AssinarAsync(Guid contratoId, int usuarioId, string ip, string? userAgent, string otp);

        Task<ContratoDetalheDto?> ObterDetalheAsync(Guid contratoId, int usuarioId);
        Task<List<ContratoResumoDto>> ListarDoContratanteAsync(int contratanteId);
        Task<List<ContratoResumoDto>> ListarDoPrestadorAsync(int prestadorId);
        Task<string> ObterUrlPdfAsync(Guid contratoId, int usuarioId);
        Task CancelarAsync(Guid contratoId, int usuarioId, string? motivo);
        Task ExpirarContratosAsync();
    }
}
