using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class CompetenciaRelacionamentoService
    {
        private readonly IPerfilProfissaoPrestadorRepository _repo;

        public CompetenciaRelacionamentoService(IPerfilProfissaoPrestadorRepository repo)
        {
            _repo = repo;
        }

        // ──────────────────────────────────────────────────────────────────────
        // ASSOCIAR
        // ──────────────────────────────────────────────────────────────────────

        public async Task AssociarCompetenciaExperienciaAsync(int prestadorId, int competenciaId, int experienciaId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var competencia = prestador.Competencias.FirstOrDefault(c => c.Id == competenciaId)
                ?? throw new NegocioException("Competência não pertence ao prestador");

            var experiencia = prestador.Experiencias.FirstOrDefault(e => e.Id == experienciaId)
                ?? throw new NegocioException("Experiência não pertence ao prestador");

            if (experiencia.CompetenciaExperiencias.Any(x => x.CompetenciaId == competenciaId))
                throw new NegocioException("Competência já vinculada a esta experiência");

            experiencia.CompetenciaExperiencias.Add(new CompetenciaExperiencia
            {
                CompetenciaId          = competenciaId,
                ExperienciaPrestadorId = experienciaId
            });

            await _repo.SaveAsync();
        }

        public async Task AssociarCompetenciaCertificacaoAsync(int prestadorId, int competenciaId, int certificacaoId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var competencia = prestador.Competencias.FirstOrDefault(c => c.Id == competenciaId)
                ?? throw new NegocioException("Competência não pertence ao prestador");

            var certificacao = prestador.Certificacoes.FirstOrDefault(c => c.Id == certificacaoId)
                ?? throw new NegocioException("Certificação não pertence ao prestador");

            if (certificacao.CompetenciaCertificacoes.Any(x => x.CompetenciaId == competenciaId))
                throw new NegocioException("Competência já vinculada a esta certificação");

            certificacao.CompetenciaCertificacoes.Add(new CompetenciaCertificacao
            {
                CompetenciaId           = competenciaId,
                CertificacaoPrestadorId = certificacaoId
            });

            await _repo.SaveAsync();
        }

        public async Task AssociarCompetenciaPortfolioAsync(int prestadorId, int competenciaId, int portfolioId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var competencia = prestador.Competencias.FirstOrDefault(c => c.Id == competenciaId)
                ?? throw new NegocioException("Competência não pertence ao prestador");

            var portfolio = prestador.Portfolio.FirstOrDefault(p => p.Id == portfolioId)
                ?? throw new NegocioException("Item de portfólio não pertence ao prestador");

            if (portfolio.CompetenciaPortfolios.Any(x => x.CompetenciaId == competenciaId))
                throw new NegocioException("Competência já vinculada a este item de portfólio");

            portfolio.CompetenciaPortfolios.Add(new CompetenciaPortfolio
            {
                CompetenciaId       = competenciaId,
                PortfolioPrestadorId = portfolioId
            });

            await _repo.SaveAsync();
        }

        // ──────────────────────────────────────────────────────────────────────
        // DESASSOCIAR
        // ──────────────────────────────────────────────────────────────────────

        public async Task DesassociarCompetenciaExperienciaAsync(int prestadorId, int competenciaId, int experienciaId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var experiencia = prestador.Experiencias.FirstOrDefault(e => e.Id == experienciaId)
                ?? throw new NegocioException("Experiência não pertence ao prestador");

            var vinculo = experiencia.CompetenciaExperiencias
                .FirstOrDefault(x => x.CompetenciaId == competenciaId);

            if (vinculo == null) return;

            experiencia.CompetenciaExperiencias.Remove(vinculo);
            await _repo.SaveAsync();
        }

        public async Task DesassociarCompetenciaCertificacaoAsync(int prestadorId, int competenciaId, int certificacaoId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var certificacao = prestador.Certificacoes.FirstOrDefault(c => c.Id == certificacaoId)
                ?? throw new NegocioException("Certificação não pertence ao prestador");

            var vinculo = certificacao.CompetenciaCertificacoes
                .FirstOrDefault(x => x.CompetenciaId == competenciaId);

            if (vinculo == null) return;

            certificacao.CompetenciaCertificacoes.Remove(vinculo);
            await _repo.SaveAsync();
        }

        public async Task DesassociarCompetenciaPortfolioAsync(int prestadorId, int competenciaId, int portfolioId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var portfolio = prestador.Portfolio.FirstOrDefault(p => p.Id == portfolioId)
                ?? throw new NegocioException("Item de portfólio não pertence ao prestador");

            var vinculo = portfolio.CompetenciaPortfolios
                .FirstOrDefault(x => x.CompetenciaId == competenciaId);

            if (vinculo == null) return;

            portfolio.CompetenciaPortfolios.Remove(vinculo);
            await _repo.SaveAsync();
        }

        // ──────────────────────────────────────────────────────────────────────
        // LISTAR por item
        // ──────────────────────────────────────────────────────────────────────

        public async Task<List<CompetenciaDTO>> ListarPorExperienciaAsync(int prestadorId, int experienciaId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var experiencia = prestador.Experiencias.FirstOrDefault(e => e.Id == experienciaId)
                ?? throw new NegocioException("Experiência não pertence ao prestador");

            return experiencia.CompetenciaExperiencias
                .Select(x => new CompetenciaDTO
                {
                    Id    = x.Competencia.Id,
                    Nome  = x.Competencia.Nome,
                    Nivel = x.Competencia.Nivel
                }).ToList();
        }

        public async Task<List<CompetenciaDTO>> ListarPorCertificacaoAsync(int prestadorId, int certificacaoId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var certificacao = prestador.Certificacoes.FirstOrDefault(c => c.Id == certificacaoId)
                ?? throw new NegocioException("Certificação não pertence ao prestador");

            return certificacao.CompetenciaCertificacoes
                .Select(x => new CompetenciaDTO
                {
                    Id    = x.Competencia.Id,
                    Nome  = x.Competencia.Nome,
                    Nivel = x.Competencia.Nivel
                }).ToList();
        }

        public async Task<List<CompetenciaDTO>> ListarPorPortfolioAsync(int prestadorId, int portfolioId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var portfolio = prestador.Portfolio.FirstOrDefault(p => p.Id == portfolioId)
                ?? throw new NegocioException("Item de portfólio não pertence ao prestador");

            return portfolio.CompetenciaPortfolios
                .Select(x => new CompetenciaDTO
                {
                    Id    = x.Competencia.Id,
                    Nome  = x.Competencia.Nome,
                    Nivel = x.Competencia.Nivel
                }).ToList();
        }

        // ──────────────────────────────────────────────────────────────────────
        // RELACIONAMENTOS de uma competência (todos os itens vinculados)
        // ──────────────────────────────────────────────────────────────────────

        public async Task<CompetenciaRelacionamentoDTO> VisualizarRelacionamentosAsync(int prestadorId, int competenciaId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var competencia = prestador.Competencias.FirstOrDefault(c => c.Id == competenciaId)
                ?? throw new NegocioException("Competência não pertence ao prestador");

            return new CompetenciaRelacionamentoDTO
            {
                Experiencias = competencia.CompetenciaExperiencias
                    .Select(x => new ExperienciaResumoDTO
                    {
                        Id      = x.ExperienciaPrestador.Id,
                        Empresa = x.ExperienciaPrestador.Empresa,
                        Cargo   = x.ExperienciaPrestador.Cargo
                    }).ToList(),

                Certificacoes = competencia.CompetenciaCertificacoes
                    .Select(x => new CertificacaoResumoDTO
                    {
                        Id   = x.CertificacaoPrestador.Id,
                        Nome = x.CertificacaoPrestador.Nome
                    }).ToList(),

                Portfolio = competencia.CompetenciaPortfolios
                    .Select(x => new PortfolioResumoDTO
                    {
                        Id     = x.PortfolioPrestador.Id,
                        Titulo = x.PortfolioPrestador.Titulo
                    }).ToList()
            };
        }
    }
}
