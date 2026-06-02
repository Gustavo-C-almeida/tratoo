using System.Net;
using System.Net.Mail;
using Tratoo.Domain.Config;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Infrastructure
{
    public class EmailService : IEmailService
    {
        public async Task EnviarCodigoVerificacaoAsync(string emailDestino, string codigo)
        {
            try
            {
                using var mensagem = new MailMessage
                {
                    From = new MailAddress(EmailConfig.Remetente, "Tratoo - Verificação de Conta"),
                    Subject = "Seu código de verificação",
                    Body = $@"
Olá!

Seu código de verificação é: {codigo}

Este código expira em 5 minutos.

Se você não solicitou este código, ignore este e-mail.

Atenciosamente,
Equipe Tratoo
",
                    IsBodyHtml = false
                };

                mensagem.To.Add(emailDestino);

                using var smtp = CriarSmtp();
                await smtp.SendMailAsync(mensagem);
            }
            catch (SmtpException ex)
            {
                throw new NegocioException($"Não foi possível enviar o e-mail de verificação. Verifique o endereço informado. Detalhe: {ex.Message}");
            }
        }

        public async Task EnviarCodigoResetSenhaAsync(string emailDestino, string codigo)
        {
            using var mensagem = CriarMensagem(
                emailDestino,
                "Redefinição de senha — Tratoo",
                $@"Olá!

Recebemos uma solicitação para redefinir a senha da sua conta na Tratoo.

Seu código de redefinição é: {codigo}

Este código expira em 5 minutos e só pode ser usado uma vez.

Se você não fez esta solicitação, ignore este e-mail. Sua senha permanece a mesma.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoPropostaEnviadaAsync(
            string emailContratante, string nomeContratante, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailContratante,
                "Nova proposta recebida no seu projeto",
                $@"Olá, {nomeContratante}!

Você recebeu uma nova proposta para o projeto: {tituloProjeto}

Acesse a plataforma para visualizar os detalhes, iniciar a negociação ou aceitar o prestador.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoContrapropostaAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                "Nova contraproposta recebida",
                $@"Olá, {nomeDestinatario}!

Você recebeu uma contraproposta no projeto: {tituloProjeto}

Acesse a plataforma para revisar os termos e responder.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoAceiteAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailPrestador,
                "Sua proposta foi aceita!",
                $@"Olá, {nomePrestador}!

Ótima notícia! Sua proposta para o projeto ""{tituloProjeto}"" foi aceita.

Acesse a plataforma para acompanhar os próximos passos e formalizar o contrato.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoRecusaAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto, string? motivo)
        {
            var motivoTexto = string.IsNullOrWhiteSpace(motivo)
                ? string.Empty
                : $"\n\nMotivo informado: {motivo}";

            using var mensagem = CriarMensagem(
                emailPrestador,
                "Proposta não aprovada",
                $@"Olá, {nomePrestador}!

Infelizmente sua proposta para o projeto ""{tituloProjeto}"" não foi aprovada.{motivoTexto}

Continue explorando outros projetos na plataforma!

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoExpiracaoAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                "Proposta expirada",
                $@"Olá, {nomeDestinatario}!

Sua proposta para o projeto ""{tituloProjeto}"" expirou sem ser aceita.

Você pode enviar uma nova proposta com prazo de validade atualizado.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarContratoGeradoAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                "Contrato gerado — aguardando assinatura",
                $@"Olá, {nomeDestinatario}!

Um contrato foi gerado para o projeto ""{tituloProjeto}"".

Acesse a plataforma para revisar os termos e assinar digitalmente. O contrato expira em 7 dias.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarSolicitacaoAssinaturaAsync(
            string emailDestinatario, string nomeDestinatario)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                "Sua assinatura é necessária",
                $@"Olá, {nomeDestinatario}!

A outra parte já assinou o contrato. Agora é a sua vez!

Acesse a plataforma e assine o contrato para que ele entre em vigor.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarContratoAtivoAsync(
            string emailDestinatario, string nomeDestinatario)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                "Contrato assinado — está em vigor!",
                $@"Olá, {nomeDestinatario}!

Ótima notícia! Ambas as partes assinaram o contrato, que agora está em vigor.

Acesse a plataforma para acompanhar o andamento do projeto.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoPagamentoConfirmadoAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valorBruto)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                $"Pagamento confirmado — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

O pagamento de R$ {valorBruto:F2} referente ao projeto ""{tituloProjeto}"" foi confirmado com sucesso.

O valor ficará retido na plataforma (escrow) até a conclusão do serviço. Após a entrega e aprovação, será liberado ao prestador.

Acesse a plataforma para acompanhar o andamento.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoPagamentoEmEscrowAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto, decimal valorLiquido)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                $"Pagamento recebido em escrow — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

O pagamento referente ao projeto ""{tituloProjeto}"" foi confirmado!

Seu valor líquido de R$ {valorLiquido:F2} está retido em escrow na plataforma e será liberado para sua conta PIX após a aprovação da entrega pelo contratante.

Conclua o serviço conforme acordado no contrato e solicite a aprovação.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarNotificacaoLiberacaoAsync(
            string emailDestinatario, string nomeDestinatario, decimal valorLiquido, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                $"Pagamento liberado — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

Ótima notícia! O valor de R$ {valorLiquido:F2} referente ao projeto ""{tituloProjeto}"" foi transferido para sua chave PIX cadastrada.

O crédito pode levar alguns instantes para aparecer na sua conta.

Obrigado por utilizar a plataforma Tratoo!

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        public async Task EnviarLembreteAvaliacaoPendenteAsync(
            string emailDestinatario, string nomeDestinatario, string tituloProjeto)
        {
            using var mensagem = CriarMensagem(
                emailDestinatario,
                $"Avaliação pendente — {tituloProjeto}",
                $@"Olá, {nomeDestinatario}!

Você ainda não avaliou o projeto ""{tituloProjeto}"".

Sua avaliação ajuda a construir um marketplace mais confiável e ajuda outros profissionais e contratantes a tomarem melhores decisões.

Acesse a plataforma para avaliar agora.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mensagem);
        }

        // ── Convite para Projeto ─────────────────────────────────────────────────

        public async Task EnviarConviteProjetoAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto,
            string nomeContratante, string? mensagem)
        {
            var msgTexto = string.IsNullOrWhiteSpace(mensagem)
                ? string.Empty
                : $"\n\nMensagem do contratante:\n\"{mensagem}\"";

            using var mail = CriarMensagem(
                emailPrestador,
                $"Convite para projeto — {tituloProjeto}",
                $@"Olá, {nomePrestador}!

{nomeContratante} gostaria de convidá-lo para o projeto ""{tituloProjeto}"".{msgTexto}

Acesse a plataforma para ver os detalhes do convite e responder.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mail);
        }

        public async Task EnviarConviteAceitoAsync(
            string emailContratante, string nomeContratante, string tituloProjeto, string nomePrestador)
        {
            using var mail = CriarMensagem(
                emailContratante,
                $"Convite aceito — {tituloProjeto}",
                $@"Olá, {nomeContratante}!

Ótima notícia! {nomePrestador} aceitou seu convite para o projeto ""{tituloProjeto}"".

Acesse o chat do projeto para iniciar a conversa e alinhar os próximos passos.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mail);
        }

        public async Task EnviarConviteRecusadoAsync(
            string emailContratante, string nomeContratante, string tituloProjeto,
            string nomePrestador, string? motivo)
        {
            var motivoTexto = string.IsNullOrWhiteSpace(motivo)
                ? string.Empty
                : $"\n\nMotivo informado: {motivo}";

            using var mail = CriarMensagem(
                emailContratante,
                $"Convite recusado — {tituloProjeto}",
                $@"Olá, {nomeContratante}!

Infelizmente {nomePrestador} não pôde aceitar seu convite para o projeto ""{tituloProjeto}"".{motivoTexto}

Confira outros prestadores compatíveis no ranking do seu projeto.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mail);
        }

        public async Task EnviarPropostaContratanteAsync(
            string emailPrestador, string nomePrestador, string tituloProjeto)
        {
            using var mail = CriarMensagem(
                emailPrestador,
                $"Proposta formal recebida — {tituloProjeto}",
                $@"Olá, {nomePrestador}!

Você recebeu uma proposta formal do contratante para o projeto ""{tituloProjeto}"".

Acesse o Tratoo para revisar os termos e aceitar ou negociar.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mail);
        }

        public async Task EnviarPropostaAceitaPrestadorAsync(
            string emailContratante, string nomeContratante, string tituloProjeto)
        {
            using var mail = CriarMensagem(
                emailContratante,
                $"Proposta aceita — {tituloProjeto}",
                $@"Olá, {nomeContratante}!

O prestador aceitou sua proposta para o projeto ""{tituloProjeto}"". O contrato foi gerado automaticamente.

Acesse o Tratoo para assinar o contrato e iniciar os pagamentos.

Atenciosamente,
Equipe Tratoo");

            using var smtp = CriarSmtp();
            await smtp.SendMailAsync(mail);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static MailMessage CriarMensagem(string destino, string assunto, string corpo)
        {
            var msg = new MailMessage
            {
                From = new MailAddress(EmailConfig.Remetente, "Tratoo"),
                Subject = assunto,
                Body = corpo,
                IsBodyHtml = false
            };
            msg.To.Add(destino);
            return msg;
        }

        private static SmtpClient CriarSmtp() =>
            new SmtpClient(EmailConfig.ServidorSmtp, EmailConfig.PortaSmtp)
            {
                Credentials = new NetworkCredential(EmailConfig.Remetente, EmailConfig.Senha),
                EnableSsl = true
            };
    }
}
