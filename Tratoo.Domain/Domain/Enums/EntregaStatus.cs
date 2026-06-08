using System.Text.Json.Serialization;

namespace Tratoo.Domain.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EntregaStatus
    {
        /// <summary>Entrega registrada pelo prestador, aguardando aprovação do contratante.</summary>
        PendenteAprovacao,
        /// <summary>Contratante aprovou a entrega. Dispara a liberação do pagamento.</summary>
        Aprovada,
        /// <summary>Contratante solicitou ajustes. O prestador pode registrar nova entrega.</summary>
        Rejeitada
    }
}
