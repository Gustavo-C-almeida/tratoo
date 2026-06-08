namespace Tratoo.Domain.Features.Avaliacoes
{
    public interface IAvaliacaoService
    {
        /// <summary>
        /// Envia lembretes por e-mail para avaliações pendentes com 3 dias (melhoria #4).
        /// Chamado pelo background service diariamente.
        /// </summary>
        Task EnviarLembretesD3Async();
        /// <summary>
        /// Cria os dois slots de avaliação (contratante ↔ prestador) logo após a liberação
        /// do pagamento. Idempotente — não cria duplicatas se chamado mais de uma vez.
        /// </summary>
        Task CriarAvaliacoesPendentesAsync(Guid contratoServicoId);

        /// <summary>
        /// Submete a avaliação preenchida pelo avaliador.
        /// Aplica blind review: publica ambas as avaliações do contrato quando o par estiver completo.
        /// </summary>
        Task EnviarAvaliacaoAsync(Guid avaliacaoId, int avaliadorId, EnviarAvaliacaoDto dto);

        /// <summary>Retorna o slot de avaliação que o usuário deve preencher para um contrato.</summary>
        Task<AvaliacaoPendenteDto?> ObterPendenteDoContratoAsync(Guid contratoServicoId, int userId);

        /// <summary>Lista todos os slots pendentes do usuário (badge de pendências).</summary>
        Task<List<AvaliacaoPendenteDto>> ListarPendentesAsync(int userId);

        /// <summary>Reputação pública (e privacidade) de um usuário.</summary>
        Task<ReputacaoDto> ObterReputacaoAsync(int avaliadoId);

        /// <summary>
        /// Avaliações públicas paginadas para o perfil de um usuário.
        /// Retorna AvaliacoesVisiveis = false quando o usuário optou por privacidade (RN-AV-005).
        /// </summary>
        Task<AvaliacoesPublicasDto> ListarPublicasAsync(int avaliadoId, int pagina, int tamanhoPagina);

        /// <summary>
        /// Altera a configuração AvaliacoesPrivado do usuário autenticado (RN-AV-001/012).
        /// Aplica-se a prestador e contratante. Efeito imediato em todas as consultas públicas.
        /// </summary>
        Task AlterarPrivacidadeAvaliacoesAsync(int userId, bool privado);

        /// <summary>
        /// Executa a expiração de slots pendentes com mais de 7 dias.
        /// Publica avaliações preenchidas, descarta slots vazios silenciosamente.
        /// Chamado pelo background service diariamente.
        /// </summary>
        Task ExpirarPendentesAsync();
    }
}
