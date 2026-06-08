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
