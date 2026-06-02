namespace Tratoo.Domain.Features.Infrastructure
{
    public interface IEmailService
    {
        Task EnviarCodigoVerificacaoAsync(string emailDestino, string codigo);
        Task EnviarCodigoResetSenhaAsync(string emailDestino, string codigo);

        // Notificações do fluxo de Proposta e Negociação
        Task EnviarNotificacaoPropostaEnviadaAsync(string emailContratante, string nomeContratante, string tituloProjeto);
        Task EnviarNotificacaoContrapropostaAsync(string emailDestinatario, string nomeDestinatario, string tituloProjeto);
        Task EnviarNotificacaoAceiteAsync(string emailPrestador, string nomePrestador, string tituloProjeto);
        Task EnviarNotificacaoRecusaAsync(string emailPrestador, string nomePrestador, string tituloProjeto, string? motivo);
        Task EnviarNotificacaoExpiracaoAsync(string emailDestinatario, string nomeDestinatario, string tituloProjeto);

        // Notificações do fluxo de Contrato
        Task EnviarContratoGeradoAsync(string emailDestinatario, string nomeDestinatario, string tituloProjeto);
        Task EnviarSolicitacaoAssinaturaAsync(string emailDestinatario, string nomeDestinatario);
        Task EnviarContratoAtivoAsync(string emailDestinatario, string nomeDestinatario);

        // Notificações do fluxo de Pagamento e Escrow
        Task EnviarNotificacaoPagamentoConfirmadoAsync(string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valorBruto);
        Task EnviarNotificacaoPagamentoEmEscrowAsync(string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valorLiquido);
        Task EnviarNotificacaoLiberacaoAsync(string emailDestinatario, string nomeDestinatario, decimal valorLiquido, string tituloProjeto);

        // Notificações do fluxo de Avaliação
        Task EnviarLembreteAvaliacaoPendenteAsync(string emailDestinatario, string nomeDestinatario, string tituloProjeto);

        // Notificações do fluxo de Convite para Projeto
        Task EnviarConviteProjetoAsync(string emailPrestador, string nomePrestador, string tituloProjeto, string nomeContratante, string? mensagem);
        Task EnviarConviteAceitoAsync(string emailContratante, string nomeContratante, string tituloProjeto, string nomePrestador);
        Task EnviarConviteRecusadoAsync(string emailContratante, string nomeContratante, string tituloProjeto, string nomePrestador, string? motivo);

        // Notificações do fluxo reverso de proposta (Contratante → Prestador)
        Task EnviarPropostaContratanteAsync(string emailPrestador, string nomePrestador, string tituloProjeto);
        Task EnviarPropostaAceitaPrestadorAsync(string emailContratante, string nomeContratante, string tituloProjeto);
    }
}
