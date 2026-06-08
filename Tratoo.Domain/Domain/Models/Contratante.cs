using System.Text.Json;
using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    public class Contratante : Usuario
    {



        // ── PJ ────────────────────────────────────────────────────────────────
        public string? Segmento { get; set; }
        public string? NomeEmpresa { get; set; }
        public string? InscricaoEstadual { get; set; }
        public string? InscricaoMunicipal { get; set; }
        public DateOnly? DataAbertura { get; set; }

        // ── Perfil público ────────────────────────────────────────────────────
        public string? Descricao { get; set; }
        public string? LogoUrl { get; set; }
        public string? SiteUrl { get; set; }
        public string? LinkedinUrl { get; set; }
        /// <summary>E-mail público de contato — diferente do e-mail de login.</summary>
        public string? EmailContato { get; set; }
        /// <summary>Texto curto sobre diferenciais e expectativas de trabalho.</summary>
        public string? PorQueTrabalharComigo { get; set; }
        /// <summary>Indica se o contratante está buscando novos prestadores.</summary>
        public DisponibilidadeContratante? Disponibilidade { get; set; }
        /// <summary>Idiomas aceitos, armazenados como JSON array (ex: ["Português","Inglês"]).</summary>
        public string? IdiomasAceitosJson { get; set; }
        /// <summary>Porte/tamanho da equipe do contratante.</summary>
        public TamanhoEquipe? TamanhoEquipe { get; set; }

        // ── Helpers de serialização ──────────────────────────────────────────
        public List<string> GetIdiomasAceitos() =>
            string.IsNullOrWhiteSpace(IdiomasAceitosJson)
                ? []
                : JsonSerializer.Deserialize<List<string>>(IdiomasAceitosJson) ?? [];

        public void SetIdiomasAceitos(IEnumerable<string> idiomas) =>
            IdiomasAceitosJson = idiomas.Any()
                ? JsonSerializer.Serialize(idiomas.Distinct().ToList())
                : null;

        // ── PF ────────────────────────────────────────────────────────────────
        /// <summary>Exibir idade publicamente no perfil (PF).</summary>
        public bool ExibirIdade { get; set; } = false;

        public bool PagadorVerificado { get; set; } = false;

        public override void VerificarPerfilMinimo()
        {
            // TipoPessoa, Endereco, IdentidadeVerificada herdados de Usuario
            // Para PJ também exige segmento e nome da empresa
            PerfilMinimoCompleto =
                TipoPessoa.HasValue &&
                IdentidadeVerificada &&
                !string.IsNullOrWhiteSpace(Endereco?.Cep) &&
                !string.IsNullOrWhiteSpace(Endereco?.Cidade) &&
                !string.IsNullOrWhiteSpace(Endereco?.Estado) &&
                (TipoPessoa == Enums.TipoPessoa.PessoaFisica ||
                 (!string.IsNullOrWhiteSpace(Segmento) && !string.IsNullOrWhiteSpace(NomeEmpresa)));
        }
    }
}
