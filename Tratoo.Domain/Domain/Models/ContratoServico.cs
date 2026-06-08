using System.Text.Json.Serialization;

namespace Tratoo.Domain.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContratoServicoStatus
    {
        /// <summary>Gerado automaticamente ao aceitar proposta. Nenhuma parte assinou ainda.</summary>
        Gerado,
        /// <summary>Ao menos uma parte assinou. Aguardando a outra.</summary>
        AguardandoAssinatura,
        /// <summary>Ambas as partes assinaram. Contrato em vigor (em execução).</summary>
        Ativo,
        /// <summary>Prestador registrou a entrega formal. Aguardando aprovação do contratante.</summary>
        AguardandoAprovacaoEntrega,
        /// <summary>Execução concluída com sucesso (entrega aprovada).</summary>
        Encerrado,
        /// <summary>Cancelado (expiração ou cancelamento manual).</summary>
        Cancelado
    }

    /// <summary>
    /// Contrato gerado automaticamente ao aceitar uma PropostaProjeto.
    /// </summary>
    public class ContratoServico
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // ── Origem ────────────────────────────────────────────────────────────────
        public int ProjetoId { get; set; }
        public Projeto Projeto { get; set; } = null!;

        public Guid PropostaId { get; set; }
        // Não FK navegacional para PropostaProjeto (evita ciclo de cascade)

        // ── Partes ────────────────────────────────────────────────────────────────
        public int ContratanteId { get; set; }
        public Contratante Contratante { get; set; } = null!;

        public int PrestadorId { get; set; }
        public Prestador.Prestador Prestador { get; set; } = null!;

        // ── Conteúdo editável (JSON) ──────────────────────────────────────────────
        /// <summary>
        /// JSON com todo o conteúdo do contrato, editável antes da primeira assinatura.
        /// Estrutura definida em ContratoConteudoDto.
        /// </summary>
        public string ConteudoJson { get; set; } = "{}";

        // ── Status ────────────────────────────────────────────────────────────────
        public ContratoServicoStatus Status { get; set; } = ContratoServicoStatus.Gerado;

        // ── Assinaturas ──────────────────────────────────────────────────────────
        public DateTime? AssinadoContratanteEm { get; set; }
        public string? IpContratante { get; set; }
        public string? UserAgentContratante { get; set; }

        public DateTime? AssinadoPrestadorEm { get; set; }
        public string? IpPrestador { get; set; }
        public string? UserAgentPrestador { get; set; }

        // ── Hash de integridade ───────────────────────────────────────────────────
        /// <summary>SHA-256 do ConteudoJson no momento da primeira assinatura. Prova que o conteúdo não foi alterado.</summary>
        public string? ConteudoHash { get; set; }

        // ── Versionamento do template ─────────────────────────────────────────────
        /// <summary>Versão do template de contrato usado na geração (ex: "v1.0-2026-05").</summary>
        public string? TemplateVersao { get; set; }

        // ── Datas ─────────────────────────────────────────────────────────────────
        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

        /// <summary>Prazo para ambas as partes assinarem. Após isso o contrato é cancelado e a proposta volta ao estado Aceita.</summary>
        public DateTime ExpiraEm { get; set; }

        // ── PDF ───────────────────────────────────────────────────────────────────
        /// <summary>Chave do arquivo PDF no bucket privado do R2. Preenchida após ambas as partes assinarem.</summary>
        public string? PdfKey { get; set; }

        // ── Snapshot ──────────────────────────────────────────────────────────────
        public ContratoSnapshot? Snapshot { get; set; }

        // ── Entrega ───────────────────────────────────────────────────────────────
        /// <summary>Preenchida quando o prestador registra a entrega do serviço.</summary>
        public DateTime? EntregaRegistradaEm { get; set; }

        // ── Cancelamento ──────────────────────────────────────────────────────────
        public string? MotivoCancelamento { get; set; }
        public int? CanceladoPorId { get; set; }
        public DateTime? CanceladoEm { get; set; }
    }
}
