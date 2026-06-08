namespace Tratoo.Domain.Features.Perfis
{
    public class UpdateProfissaoPrestadorDTO
    {
        public int PrestadorId { get; set; }

        public string? TituloProfissional { get; set; }
        public string? AreaEspecializacao { get; set; }
        public string? FuncaoExecutada { get; set; }
        public string? Descricao { get; set; }

        public string? LinkedinUrl { get; set; }
        public string? PortfolioUrl { get; set; }
        public string? EmailContato { get; set; }

        /// <summary>JSON com até 3 links extras: [{\"titulo\":\"X\",\"url\":\"Y\"}]</summary>
        public string? OutrosLinks { get; set; }

        public string? Telefone { get; set; }
    }
}
