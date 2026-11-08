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
    /// Carrega o contexto completo para log estruturado e para o PagamentoService
    /// decidir o estado de falha correto.
    /// </summary>
    public class AsaasIntegracaoException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string RespostaBody { get; }
        public AsaasErroTipo TipoErro { get; }

        public AsaasIntegracaoException(
            HttpStatusCode statusCode,
            string respostaBody,
            AsaasErroTipo tipoErro,
            string message)
            : base(message)
        {
            StatusCode   = statusCode;
            RespostaBody = respostaBody;
            TipoErro     = tipoErro;
        }

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
