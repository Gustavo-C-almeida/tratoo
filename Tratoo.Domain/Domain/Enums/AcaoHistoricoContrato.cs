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
        DisputaResolvida,

        /// <summary>
        /// Informativo, NÃO é falha: na aprovação, a liberação imediata perdeu a reivindicação do
        /// pagamento para outra operação (ex.: o job de liberação automática), que o processou.
        /// Esta aprovação não iniciou transferência. Gravado como texto: sem migration.
        /// </summary>
        LiberacaoProcessadaPorOutraOperacao
    }
}
