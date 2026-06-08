namespace Tratoo.Domain.Features.Perfis
{
    public class CertificacaoDTO
    {
        public int Id { get; set; }
        public int PrestadorId { get; set; }

        public string Nome { get; set; } = string.Empty;
        public string Instituicao { get; set; } = string.Empty;

        public DateTime DataEmissao { get; set; }
        public DateTime? DataValidade { get; set; }

        public string? LinkVerificacao { get; set; }

        /// <summary>URL do certificado anexado (PDF ou imagem).</summary>
        public string? ArquivoUrl { get; set; }

        public List<CompetenciaDTO> Competencias { get; set; } = new();
    }
}
