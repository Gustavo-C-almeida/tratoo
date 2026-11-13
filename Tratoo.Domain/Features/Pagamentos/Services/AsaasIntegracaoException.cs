using System.Net;

namespace Tratoo.Domain.Features.Pagamentos
{
    /// <summary>
    /// Classifica a recuperabilidade de falhas da integração Asaas para orientar
    /// o tratamento no PagamentoService: reverter para Retido (recuperável) ou
    /// marcar como FalhaTransferencia permanente (não recuperável).
    /// </summary>
    public enum AsaasErroTipo
    {
        /// <summary>Erro recuperável via reprocessamento (timeout, 5xx, falha de rede).</summary>
        GatewayIndisponivel,

        /// <summary>Payload inválido, mas a causa pode ser corrigida (ex.: descrição longa).</summary>
        PayloadInvalido,

        /// <summary>Chave PIX não existe, inválida ou sem conta destino — exige correção cadastral.</summary>
        ChavePixInvalida,

        /// <summary>Regra de negócio rejeitada pelo Asaas (422) — requer revisão administrativa.</summary>
        RegraDeNegocio,

        /// <summary>Saldo insuficiente na conta Asaas da plataforma.</summary>
        SaldoInsuficiente,

        /// <summary>Outro erro HTTP 4xx não classificado.</summary>
        ClienteError,

        /// <summary>
        /// Não se sabe se a operação foi criada no Asaas: timeout, falha de rede, 5xx,
        /// resposta 2xx ilegível ou um status que a documentação do endpoint não descreve.
        /// Nunca é recuperável automaticamente — repetir a chamada pode duplicar a operação.
        /// "Timeout não significa que a cobrança não foi criada" (docs.asaas.com, Biblioteca
        /// de Erros → "Cobrança duplicada após retry sem idempotência").
        /// </summary>
        ResultadoIncerto,
    }

    public static class AsaasErroTipoExtensions
    {
        /// <summary>Indica se o erro permite reprocessamento automático após correção.</summary>
        public static bool EhRecuperavel(this AsaasErroTipo tipo) => tipo switch
        {
            AsaasErroTipo.GatewayIndisponivel => true,
            AsaasErroTipo.PayloadInvalido     => true,   // descrição pode ser corrigida
            _                                 => false
        };
    }

    /// <summary>
    /// Exceção lançada pelo AsaasGatewayService quando a API Asaas retorna erro.
    /// Carrega o contexto para log estruturado e para o PagamentoService decidir o estado
    /// de falha correto. O corpo bruto da resposta NÃO é guardado: ele pode trazer chave
    /// PIX, CPF/CNPJ e dados do favorecido, e esta exceção chega a logs e ao ledger.
    /// </summary>
    public class AsaasIntegracaoException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        /// <summary>Resumo seguro do corpo (<see cref="ResumirResposta"/>), nunca o corpo.</summary>
        public string ResumoResposta { get; }
        public AsaasErroTipo TipoErro { get; }

        /// <param name="respostaBody">Corpo bruto; usado só para o resumo, não é guardado.</param>
        /// <param name="message">Não deve conter o corpo nem dado pessoal.</param>
        public AsaasIntegracaoException(
            HttpStatusCode statusCode,
            string respostaBody,
            AsaasErroTipo tipoErro,
            string message)
            : base(message)
        {
            StatusCode     = statusCode;
            ResumoResposta = ResumirResposta(respostaBody);
            TipoErro       = tipoErro;
        }

        /// <summary>
        /// Descrição de uma resposta do Asaas segura para log, exceção e ledger: só os
        /// <c>code</c> de <c>errors[]</c> (identificadores fixos da API) e o tamanho do corpo.
        /// Nunca o texto: <c>description</c> e os demais campos podem ecoar chave PIX,
        /// CPF/CNPJ, nome ou e-mail.
        /// </summary>
        public static string ResumirResposta(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return "corpo vazio";

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("errors", out var erros)
                    && erros.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var codigos = erros.EnumerateArray()
                        .Select(e => e.ValueKind == System.Text.Json.JsonValueKind.Object
                                     && e.TryGetProperty("code", out var c)
                                     && c.ValueKind == System.Text.Json.JsonValueKind.String
                                ? CodigoSeguro(c.GetString())
                                : "?")
                        .Take(5)
                        .ToList();
                    return codigos.Count == 0
                        ? $"errors[] vazio ({body.Length} bytes)"
                        : $"códigos de erro: {string.Join(", ", codigos)} ({body.Length} bytes)";
                }
                return $"JSON sem errors[] ({body.Length} bytes)";
            }
            catch (System.Text.Json.JsonException)
            {
                return $"corpo não-JSON ({body.Length} bytes)";
            }
        }

        // Um code da API é um identificador (ex.: invalid_value). Qualquer outra coisa
        // — inclusive um valor ecoado — não passa.
        private static string CodigoSeguro(string? codigo) =>
            !string.IsNullOrEmpty(codigo) && codigo.Length <= 60
            && codigo.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.')
                ? codigo
                : "?";

        /// <summary>
        /// Classificação de uma resposta não-2xx de <c>POST /v3/transfers</c>, onde o erro
        /// decide se o dinheiro pode ter saído.
        ///
        /// Só as respostas que a documentação oficial descreve para esse endpoint comprovam
        /// que a transferência NÃO foi criada (docs.asaas.com, referência "Transferir para
        /// conta de outra Instituição ou chave Pix", consultada em 08/11/2026):
        ///  • 400 com corpo <c>{"errors":[...]}</c> (ErrorResponseDTO) — validação recusada;
        ///  • 401 — chave de API inválida, a requisição nem é autenticada.
        /// Todo o resto (5xx, 403, 404, 408, 409, 422, 429, 400 sem <c>errors</c>) não tem
        /// evidência documentada e vira <see cref="AsaasErroTipo.ResultadoIncerto"/>.
        /// Ampliar esta lista exige evidência (documentação ou resposta observada).
        /// </summary>
        public static AsaasErroTipo ClassificarTransferencia(HttpStatusCode status, string body)
        {
            if (status == HttpStatusCode.Unauthorized)
                return AsaasErroTipo.ClienteError;

            if (status == HttpStatusCode.BadRequest && TemListaDeErros(body))
                return Classificar(status, body);

            return AsaasErroTipo.ResultadoIncerto;
        }

        /// <summary>
        /// Classificação de uma resposta não-2xx de <c>POST /v3/payments/{id}/refund</c>, onde o
        /// erro decide se o dinheiro pode ter voltado ao pagador. Aplica a MESMA regra de evidência
        /// de <see cref="ClassificarTransferencia"/>: só 400 com <c>{"errors":[...]}</c> e 401
        /// comprovam que o estorno NÃO foi feito. Todo o resto vira
        /// <see cref="AsaasErroTipo.ResultadoIncerto"/> — e um estorno incerto nunca pode ser
        /// tratado como "não aconteceu" (o pagamento não pode voltar a ficar liberável).
        /// </summary>
        public static AsaasErroTipo ClassificarEstorno(HttpStatusCode status, string body)
            => ClassificarTransferencia(status, body);

        private static bool TemListaDeErros(string body)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("errors", out var erros)
                    && erros.ValueKind == System.Text.Json.JsonValueKind.Array
                    && erros.GetArrayLength() > 0;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Classifica a resposta HTTP do Asaas em um AsaasErroTipo com base no código
        /// e no conteúdo do body. Regra de classificação centralizada.
        /// </summary>
        public static AsaasErroTipo Classificar(HttpStatusCode status, string body)
        {
            var bodyLower = body.ToLowerInvariant();

            if ((int)status >= 500)
                return AsaasErroTipo.GatewayIndisponivel;

            if (status == HttpStatusCode.UnprocessableEntity)
                return AsaasErroTipo.RegraDeNegocio;

            if (status == HttpStatusCode.BadRequest)
            {
                if (bodyLower.Contains("description") || bodyLower.Contains("descri"))
                    return AsaasErroTipo.PayloadInvalido;

                if (bodyLower.Contains("pix") || bodyLower.Contains("key") ||
                    bodyLower.Contains("chave") || bodyLower.Contains("address"))
                    return AsaasErroTipo.ChavePixInvalida;

                if (bodyLower.Contains("balance") || bodyLower.Contains("saldo"))
                    return AsaasErroTipo.SaldoInsuficiente;

                return AsaasErroTipo.ClienteError;
            }

            // 401, 403, 404 etc.
            return AsaasErroTipo.ClienteError;
        }
    }
}
