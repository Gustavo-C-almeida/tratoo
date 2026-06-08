namespace Tratoo.Domain.Features.Perfis
{
    public interface IDadosBancariosService
    {
        /// <summary>Retorna os dados bancários mascarados do prestador (nunca o valor completo).</summary>
        Task<DadosBancariosViewDTO> ObterAsync(int prestadorId);

        /// <summary>Gera e envia por e-mail um token de uso único (10 min) para revalidação.</summary>
        Task SolicitarAlteracaoAsync(int prestadorId, string ip);

        /// <summary>Valida o token (com proteção a brute force) e libera a edição temporária.</summary>
        Task ConfirmarTokenAsync(int prestadorId, string token, string ip);

        /// <summary>Salva os dados bancários — exige confirmação de token prévia. Retorna a visão mascarada.</summary>
        Task<DadosBancariosViewDTO> AtualizarAsync(int prestadorId, AtualizarDadosBancariosDTO dto, string ip);
    }
}
