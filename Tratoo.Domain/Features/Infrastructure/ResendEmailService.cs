using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tratoo.Domain.Config;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Infrastructure
{
    /// <summary>
    /// Implementação de <see cref="IEmailService"/> sobre a API HTTPS do Resend
    /// (POST /emails). Substitui a antiga implementação SMTP/Gmail, que não
    /// funciona no Railway Trial — o plano bloqueia SMTP outbound, mas HTTPS/443
    /// segue liberado.
    ///
    /// Todo o conteúdo das mensagens (assunto/corpo) foi preservado da
    /// implementação anterior: só o transporte mudou. Para trocar de provedor,
    /// basta criar outra implementação de IEmailService e reapontar o DI.
    ///
    /// Registrado como HttpClient tipado (AddHttpClient) — o handler é reciclado
    /// pelo IHttpClientFactory, evitando socket exhaustion e DNS obsoleto.
    /// </summary>
    public class ResendEmailService : IEmailService
    {
        private readonly HttpClient _http;
        private readonly ResendSettings _settings;
        private readonly ILogger<ResendEmailService> _logger;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ResendEmailService(
            HttpClient http,
            IOptions<ResendSettings> options,
            ILogger<ResendEmailService> logger)
        {
            _http = http;
            _settings = options.Value;
            _logger = logger;

            // Falha explícita de configuração — a mensagem cita o NOME da variável,
            // nunca o valor, para não vazar a credencial em log/stack trace.
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
                throw new InvalidOperationException(
                    "Resend não configurado: defina a variável de ambiente RESEND_API_KEY (ou Resend:ApiKey no appsettings).");

            if (string.IsNullOrWhiteSpace(_settings.FromEmail))
                throw new InvalidOperationException(
                    "Resend não configurado: defina a variável de ambiente RESEND_FROM_EMAIL (ou Resend:FromEmail no appsettings).");

            _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
            _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSegundos);
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            _http.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
        /// Único ponto de saída para a API do Resend. Corpo em texto puro (text),
        /// equivalente ao IsBodyHtml = false do SMTP anterior.
        /// </summary>
        private async Task EnviarAsync(string destino, string assunto, string corpo)
        {
            var payload = new
            {
                from = $"{_settings.FromName} <{_settings.FromEmail}>",
                to = new[] { destino },
                subject = assunto,
                text = corpo
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOpts), Encoding.UTF8, "application/json");

            HttpResponseMessage resposta;
            try
            {
                resposta = await _http.PostAsync("emails", content);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Timeout ou falha de rede. Não expõe detalhe de infraestrutura ao usuário.
                _logger.LogError(ex,
                    "Falha de rede/timeout ao enviar e-mail via Resend. Destino: {Destino}, Assunto: {Assunto}",
                    MascararEmail(destino), assunto);

                throw new NegocioException(
                    "Não foi possível enviar o e-mail no momento. Tente novamente em instantes.");
            }

            var body = await resposta.Content.ReadAsStringAsync();

            if (!resposta.IsSuccessStatusCode)
            {
                // O body do Resend traz { "name", "message", "statusCode" } e nunca ecoa a API key.
                _logger.LogError(
                    "Resend rejeitou o envio. Status: {StatusCode}, Destino: {Destino}, Assunto: {Assunto}, Resposta: {Body}",
                    (int)resposta.StatusCode, MascararEmail(destino), assunto, body);

                throw new NegocioException(
                    $"Não foi possível enviar o e-mail. Detalhe: {ExtrairMensagemErro(body, resposta.StatusCode)}");
            }

            _logger.LogInformation(
                "E-mail enviado via Resend. Destino: {Destino}, Assunto: {Assunto}, Id: {Id}",
                MascararEmail(destino), assunto, ExtrairId(body));
        }

        /// <summary>Extrai o campo "message" do erro do Resend, com fallback no status HTTP.</summary>
        private static string ExtrairMensagemErro(string body, System.Net.HttpStatusCode status)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? $"HTTP {(int)status}";
            }
            catch (JsonException)
            {
                // corpo não-JSON (ex.: HTML de proxy) — cai no fallback
            }

            return $"HTTP {(int)status}";
        }

        /// <summary>Id da mensagem no Resend, útil para rastrear entregas no painel.</summary>
        private static string ExtrairId(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("id", out var id)
                    ? id.GetString() ?? "?"
                    : "?";
            }
            catch (JsonException)
            {
                return "?";
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
