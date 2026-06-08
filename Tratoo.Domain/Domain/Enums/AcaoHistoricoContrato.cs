using System.Text.Json.Serialization;

namespace Tratoo.Domain.Enums
{
    /// <summary>Ações auditáveis registradas em HistoricoContrato.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AcaoHistoricoContrato
    {
        EntregaCriada,
        AnexoAdicionado,
        LinkAdicionado,
        EntregaAprovada,
        EntregaRejeitada,
        PagamentoLiberado,
        FalhaLiberacao,
        DisputaResolvida
    }
}
