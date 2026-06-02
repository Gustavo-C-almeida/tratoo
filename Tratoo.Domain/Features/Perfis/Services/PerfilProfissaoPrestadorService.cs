using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class PerfilProfissaoPrestadorService
    {
        private readonly IPerfilProfissaoPrestadorRepository _repo;
        private readonly PrestadorIndexadorService _indexador;

        public PerfilProfissaoPrestadorService(
            IPerfilProfissaoPrestadorRepository repo,
            PrestadorIndexadorService indexador)
        {
            _repo = repo;
            _indexador = indexador;
        }

        public async Task AtualizarPerfilAsync(UpdateProfissaoPrestadorDTO dto)
        {
            var p = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            p.TituloProfissional = dto.TituloProfissional;
            p.AreaEspecializacao = dto.AreaEspecializacao;
            p.FuncaoExecutada    = dto.FuncaoExecutada;
            p.Descricao          = dto.Descricao;
            p.LinkedinUrl        = dto.LinkedinUrl;
            p.PortfolioUrl       = dto.PortfolioUrl;
            p.GitHubUrl          = dto.GitHubUrl;
            p.EmailContato       = dto.EmailContato;
            p.OutrosLinks        = dto.OutrosLinks;
            p.ValorHora          = dto.ValorHora;
            p.Telefone           = dto.Telefone;

            p.PorcentagemCompleto = CalcularCompletude(p);

            await _repo.SaveAsync();

            // Bio e título são os campos com maior impacto no embedding
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task AtualizarFotoAsync(int prestadorId, string fotoUrl)
        {
            var p = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            p.FotoUrl = fotoUrl;
            p.PorcentagemCompleto = CalcularCompletude(p);

            await _repo.SaveAsync();
            // Foto não altera o texto normalizado — sem reindexação necessária
        }

        /// <summary>
        /// Calcula a completude do perfil vitrine (0–100).
        /// Regras conforme documento: Perfil Prestador MVP v1.0.
        /// </summary>
        public static int CalcularCompletude(Prestador p)
        {
            int score = 0;

            if (!string.IsNullOrEmpty(p.FotoUrl))
                score += 10;

            if ((p.TituloProfissional?.Length ?? 0) >= 5)
                score += 10;

            if ((p.Descricao?.Length ?? 0) >= 100)
                score += 15;

            if (p.Competencias.Count >= 3)
                score += 15;

            if (p.Portfolio.Count >= 1)
                score += 20;

            if (p.Experiencias.Count >= 1)
                score += 15;

            if (p.Certificacoes.Count >= 1)
                score += 10;

            if (p.ValorHora.HasValue)
                score += 5;

            return score;
        }
    }
}
