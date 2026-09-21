using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;
using Tratoo.Domain.Config;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Infrastructure
{
    /// <summary>
    /// Implementação de <see cref="IEmailService"/> sobre o SDK oficial do Resend
    /// (pacote NuGet "Resend"). Substitui a antiga implementação SMTP/Gmail, que
    /// não funciona no Railway Trial — o plano bloqueia SMTP outbound, mas
    /// HTTPS/443 (usado pela API do Resend) segue liberado.
    ///
    /// Todo o conteúdo das mensagens (assunto/corpo) foi preservado da
    /// implementação SMTP anterior: só o transporte mudou. Para trocar de
    /// provedor, basta criar outra implementação de IEmailService e reapontar
    /// o DI (ver Program.cs).
    ///
    /// O IResend injetado já é um HttpClient tipado gerenciado pelo
    /// IHttpClientFactory — registrado via AddResend() em Program.cs, o SDK faz
    /// isso internamente (AddHttpClient&lt;IResend, ResendClient&gt;). Nenhum
    /// HttpClient é criado manualmente aqui.
    /// </summary>
    public class ResendEmailService : IEmailService
    {
        private readonly IResend _resend;
        private readonly ResendSettings _settings;
        private readonly ILogger<ResendEmailService> _logger;

        public ResendEmailService(
            IResend resend,
            IOptions<ResendSettings> options,
            ILogger<ResendEmailService> logger)
        {
            _resend = resend;
            _settings = options.Value;
            _logger = logger;

            // Falha explícita de configuração — a mensagem cita o NOME da variável,
            // nunca o valor, para não vazar a credencial em log/stack trace.
            if (string.IsNullOrWhiteSpace(_settings.FromEmail))
                throw new InvalidOperationException(
                    "Resend não configurado: defina a variável de ambiente RESEND_FROM_EMAIL (ou Resend:FromEmail no appsettings).");
        }

        public Task EnviarCodigoVerificacaoAsync(string emailDestino, string codigo) =>
            EnviarAsync(
                emailDestino,
                "Seu código de verificação",
                $@"Olá!

Seu código de verificação é: {codigo}

Este código expira em 5 minutos.

Se você não solicitou este código, ignore este e-mail.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarCodigoResetSenhaAsync(string emailDestino, string codigo) =>
            EnviarAsync(
                emailDestino,
                "Redefinição de senha — Tratoo",
                $@"Olá!

Recebemos uma solicitação para redefinir a senha da sua conta na Tratoo.

Seu código de redefinição é: {codigo}

Este código expira em 5 minutos e só pode ser usado uma vez.

Se você não fez esta solicitação, ignore este e-mail. Sua senha permanece a mesma.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarTokenDadosBancariosAsync(string emailDestino, string nome, string token) =>
            EnviarAsync(
                emailDestino,
                "Confirmação de alteração de dados bancários — Tratoo",
                $@"Olá, {nome}!

Recebemos uma solicitação para alterar os dados bancários (chave PIX) da sua conta na Tratoo.

Seu código de confirmação é: {token}

Este código expira em 10 minutos e só pode ser usado uma vez.

⚠️ Se você NÃO solicitou esta alteração, ignore este e-mail e altere sua senha imediatamente —
alguém pode ter acesso indevido à sua conta. Seus dados bancários permanecem inalterados.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoPropostaEnviadaAsync(
            string emailContratante, string nomeContratante, string tituloProjeto) =>
            EnviarAsync(
                emailContratante,
                "Nova proposta recebida no seu projeto",
                $@"Olá, {nomeContratante}!

Você recebeu uma nova proposta para o projeto: {tituloProjeto}

Acesse a plataforma para visualizar os detalhes, iniciar a negociação ou aceitar o prestador.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoContrapropostaAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto) =>
            EnviarAsync(
                emailDestinatario,
                "Nova contraproposta recebida",
                $@"Olá, {nomeDestinatario}!

Você recebeu uma contraproposta no projeto: {tituloProjeto}

Acesse a plataforma para revisar os termos e responder.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoAceiteAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto) =>
            EnviarAsync(
                emailPrestador,
                "Sua proposta foi aceita!",
                $@"Olá, {nomePrestador}!

Ótima notícia! Sua proposta para o projeto ""{tituloProjeto}"" foi aceita.

Acesse a plataforma para acompanhar os próximos passos e formalizar o contrato.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoRecusaAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto, string? motivo)
        {
            var motivoTexto = string.IsNullOrWhiteSpace(motivo)
                ? string.Empty
                : $"\n\nMotivo informado: {motivo}";

            return EnviarAsync(
                emailPrestador,
                "Proposta não aprovada",
                $@"Olá, {nomePrestador}!

Infelizmente sua proposta para o projeto ""{tituloProjeto}"" não foi aprovada.{motivoTexto}

Continue explorando outros projetos na plataforma!

Atenciosamente,
Equipe Tratoo");
        }

        public Task EnviarNotificacaoExpiracaoAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto) =>
            EnviarAsync(
                emailDestinatario,
                "Proposta expirada",
                $@"Olá, {nomeDestinatario}!

Sua proposta para o projeto ""{tituloProjeto}"" expirou sem ser aceita.

Você pode enviar uma nova proposta com prazo de validade atualizado.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarContratoGeradoAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto) =>
            EnviarAsync(
                emailDestinatario,
                "Contrato gerado — aguardando assinatura",
                $@"Olá, {nomeDestinatario}!

Um contrato foi gerado para o projeto ""{tituloProjeto}"".

Acesse a plataforma para revisar os termos e assinar digitalmente. O contrato expira em 7 dias.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarSolicitacaoAssinaturaAsync(
            string emailDestinatario, string nomeDestinatario) =>
            EnviarAsync(
                emailDestinatario,
                "Sua assinatura é necessária",
                $@"Olá, {nomeDestinatario}!

A outra parte já assinou o contrato. Agora é a sua vez!

Acesse a plataforma e assine o contrato para que ele entre em vigor.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarContratoAtivoAsync(
            string emailDestinatario, string nomeDestinatario) =>
            EnviarAsync(
                emailDestinatario,
                "Contrato assinado — está em vigor!",
                $@"Olá, {nomeDestinatario}!

Ótima notícia! Ambas as partes assinaram o contrato, que agora está em vigor.

Acesse a plataforma para acompanhar o andamento do projeto.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarOtpAssinaturaAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto, string otp) =>
            EnviarAsync(
                emailDestinatario,
                $"Seu código de assinatura — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

Você solicitou a assinatura digital do contrato referente ao projeto ""{tituloProjeto}"".

Seu código de confirmação é:

    {otp}

Este código é válido por 10 minutos e pode ser usado apenas uma vez.

Se você não solicitou esta assinatura, ignore este e-mail e entre em contato com o suporte imediatamente.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoPagamentoConfirmadoAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valorBruto) =>
            EnviarAsync(
                emailDestinatario,
                $"Pagamento confirmado — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

O pagamento de R$ {valorBruto:F2} referente ao projeto ""{tituloProjeto}"" foi confirmado com sucesso.

O valor ficará retido na plataforma (escrow) até a conclusão do serviço. Após a entrega e aprovação, será liberado ao prestador.

Acesse a plataforma para acompanhar o andamento.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoPagamentoEmEscrowAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valor) =>
            EnviarAsync(
                emailDestinatario,
                $"Pagamento recebido em escrow — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

O pagamento referente ao projeto ""{tituloProjeto}"" foi confirmado!

O valor de R$ {valor:F2} está retido em escrow e será liberado integralmente para sua conta PIX após a aprovação da entrega pelo contratante.

Conclua o serviço conforme acordado no contrato e solicite a aprovação.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoLiberacaoAsync(
            string emailDestinatario, string nomeDestinatario, decimal valor, string tituloProjeto) =>
            EnviarAsync(
                emailDestinatario,
                $"Pagamento liberado — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

Ótima notícia! O valor de R$ {valor:F2} referente ao projeto ""{tituloProjeto}"" foi transferido integralmente para sua chave PIX cadastrada.

O crédito pode levar alguns instantes para aparecer na sua conta.

Obrigado por utilizar a plataforma Tratoo!

Atenciosamente,
Equipe Tratoo");

        public Task EnviarNotificacaoFalhaTransferenciaAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valor) =>
            EnviarAsync(
                emailDestinatario,
                $"Atenção: falha na transferência do pagamento — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

Identificamos uma falha técnica durante a transferência do seu pagamento referente ao projeto ""{tituloProjeto}"".

O valor de R$ {valor:F2} continua protegido em nossa plataforma e não foi perdido. Nossa equipe está realizando o tratamento necessário e o reprocessamento será efetuado em breve.

Você receberá uma nova notificação quando a transferência for concluída.

Se tiver dúvidas ou quiser acompanhar o status, entre em contato com nosso suporte.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarLembreteAvaliacaoPendenteAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto) =>
            EnviarAsync(
                emailDestinatario,
                $"Avaliação pendente — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

Você ainda não avaliou o projeto ""{tituloProjeto}"".

Sua avaliação ajuda a construir um marketplace mais confiável e ajuda outros profissionais e contratantes a tomarem melhores decisões.

Acesse a plataforma para avaliar agora.

Atenciosamente,
Equipe Tratoo");

        // ── Convite para Projeto ─────────────────────────────────────────────────

        public Task EnviarConviteProjetoAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto,
            string nomeContratante, string? mensagem)
        {
            var msgTexto = string.IsNullOrWhiteSpace(mensagem)
                ? string.Empty
                : $"\n\nMensagem do contratante:\n\"{mensagem}\"";

            return EnviarAsync(
                emailPrestador,
                $"Convite para projeto — {tituloProjeto}",
                $@"Olá, {nomePrestador}!

{nomeContratante} gostaria de convidá-lo para o projeto ""{tituloProjeto}"".{msgTexto}

Acesse a plataforma para ver os detalhes do convite e responder.

Atenciosamente,
Equipe Tratoo");
        }

        public Task EnviarConviteAceitoAsync(
            string emailContratante, string nomeContratante, string tituloProjeto, string nomePrestador) =>
            EnviarAsync(
                emailContratante,
                $"Convite aceito — {tituloProjeto}",
                $@"Olá, {nomeContratante}!

Ótima notícia! {nomePrestador} aceitou seu convite para o projeto ""{tituloProjeto}"".

Acesse o chat do projeto para iniciar a conversa e alinhar os próximos passos.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarConviteRecusadoAsync(
            string emailContratante, string nomeContratante, string tituloProjeto,
            string nomePrestador, string? motivo)
        {
            var motivoTexto = string.IsNullOrWhiteSpace(motivo)
                ? string.Empty
                : $"\n\nMotivo informado: {motivo}";

            return EnviarAsync(
                emailContratante,
                $"Convite recusado — {tituloProjeto}",
                $@"Olá, {nomeContratante}!

Infelizmente {nomePrestador} não pôde aceitar seu convite para o projeto ""{tituloProjeto}"".{motivoTexto}

Confira outros prestadores compatíveis no ranking do seu projeto.

Atenciosamente,
Equipe Tratoo");
        }

        public Task EnviarPropostaContratanteAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto) =>
            EnviarAsync(
                emailPrestador,
                $"Proposta formal recebida — {tituloProjeto}",
                $@"Olá, {nomePrestador}!

Você recebeu uma proposta formal do contratante para o projeto ""{tituloProjeto}"".

Acesse o Tratoo para revisar os termos e aceitar ou negociar.

Atenciosamente,
Equipe Tratoo");

        public Task EnviarPropostaAceitaPrestadorAsync(
            string emailContratante, string nomeContratante, string tituloProjeto) =>
            EnviarAsync(
                emailContratante,
                $"Proposta aceita — {tituloProjeto}",
                $@"Olá, {nomeContratante}!

O prestador aceitou sua proposta para o projeto ""{tituloProjeto}"". O contrato foi gerado automaticamente.

Acesse o Tratoo para assinar o contrato e iniciar os pagamentos.

Atenciosamente,
Equipe Tratoo");

        // ── Transporte ───────────────────────────────────────────────────────────

        /// <summary>
        /// Único ponto de saída para o SDK do Resend. Corpo em texto puro (TextBody),
        /// equivalente ao IsBodyHtml = false do SMTP anterior.
        /// </summary>
        private async Task EnviarAsync(string destino, string assunto, string corpo)
        {
            var mensagem = new EmailMessage
            {
                From = new EmailAddress { Email = _settings.FromEmail, DisplayName = _settings.FromName },
                To = destino,
                Subject = assunto,
                TextBody = corpo
            };

            try
            {
                var resposta = await _resend.EmailSendAsync(mensagem);

                _logger.LogInformation(
                    "E-mail enviado via Resend. Destino: {Destino}, Assunto: {Assunto}, Id: {Id}",
                    MascararEmail(destino), assunto, resposta.Content);
            }
            catch (ResendException ex)
            {
                // ex.Message vem do corpo de erro do Resend (name/message/statusCode)
                // e nunca ecoa a API key — ela só é usada no header Authorization.
                _logger.LogError(ex,
                    "Resend rejeitou o envio. Status: {StatusCode}, ErroTipo: {ErrorType}, Transitório: {Transiente}, Destino: {Destino}, Assunto: {Assunto}",
                    ex.StatusCode, ex.ErrorType, ex.IsTransient, MascararEmail(destino), assunto);

                throw new NegocioException($"Não foi possível enviar o e-mail. Detalhe: {ex.Message}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Timeout ou falha de rede antes mesmo de obter resposta do Resend.
                _logger.LogError(ex,
                    "Falha de rede/timeout ao enviar e-mail via Resend. Destino: {Destino}, Assunto: {Assunto}",
                    MascararEmail(destino), assunto);

                throw new NegocioException(
                    "Não foi possível enviar o e-mail no momento. Tente novamente em instantes.");
            }
        }

        /// <summary>
        /// Mascara o e-mail para log — preserva a inicial e o domínio, no mesmo espírito
        /// do MascararChave() usado no gateway de pagamento.
        /// </summary>
        private static string MascararEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "****";

            var arroba = email.IndexOf('@');
            if (arroba <= 0) return "****";

            var local = email[..arroba];
            var dominio = email[arroba..];

            return local.Length == 1
                ? $"*{dominio}"
                : $"{local[0]}{new string('*', local.Length - 1)}{dominio}";
        }
    }
}
