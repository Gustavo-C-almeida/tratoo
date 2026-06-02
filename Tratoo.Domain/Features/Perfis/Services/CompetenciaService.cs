using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.Perfis
{
    public class CompetenciaService
    {
        private readonly IPrestadorRepository _repo;
        private readonly PrestadorIndexadorService _indexador;

        public CompetenciaService(IPrestadorRepository repo, PrestadorIndexadorService indexador)
        {
            _repo = repo;
            _indexador = indexador;
        }

        public async Task<List<CompetenciaDTO>> VisualizarAsync(int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            return prestador.Competencias
                .Select(c => new CompetenciaDTO
                {
                    Id          = c.Id,
                    PrestadorId = c.PrestadorId,
                    Nome        = c.Nome,
                    Nivel       = c.Nivel,
                })
                .ToList();
        }

        public async Task AdicionarAsync(CompetenciaDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Nome))
                throw new NegocioException("O nome da competência é obrigatório.");

            if (dto.Nome.Length > 80)
                throw new NegocioException("O nome da competência deve ter no máximo 80 caracteres.");

            if (dto.Nivel < 1 || dto.Nivel > 5)
                throw new NegocioException("O nível deve ser entre 1 (Básico) e 5 (Especialista).");

            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var nomeNormalizado = dto.Nome.Trim();
            if (prestador.Competencias.Any(c => string.Equals(c.Nome, nomeNormalizado, StringComparison.OrdinalIgnoreCase)))
                throw new NegocioException("Já existe uma competência com este nome.");

            prestador.Competencias.Add(new Competencia
            {
                PrestadorId = dto.PrestadorId,
                Nome        = nomeNormalizado,
                Nivel       = dto.Nivel
            });

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task EditarAsync(int prestadorId, int competenciaId, int nivel)
        {
            if (nivel < 1 || nivel > 5)
                throw new NegocioException("O nível deve ser entre 1 (Básico) e 5 (Especialista).");

            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var competencia = prestador.Competencias.FirstOrDefault(c => c.Id == competenciaId)
                ?? throw new NegocioException("Competência não encontrada para este prestador");

            competencia.Nivel = nivel;

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(prestadorId);
        }

        public async Task RemoverAsync(int competenciaId, int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var comp = prestador.Competencias.FirstOrDefault(c => c.Id == competenciaId);

            // Já foi removida — DELETE é idempotente
            if (comp == null) return;

            // Remove os registros M:N antes de remover a competência para satisfazer
            // as FK constraints (OnDelete: Restrict nas tabelas de junção).
            foreach (var exp in prestador.Experiencias)
            {
                var links = exp.CompetenciaExperiencias.Where(x => x.CompetenciaId == competenciaId).ToList();
                foreach (var link in links) exp.CompetenciaExperiencias.Remove(link);
            }

            foreach (var cert in prestador.Certificacoes)
            {
                var links = cert.CompetenciaCertificacoes.Where(x => x.CompetenciaId == competenciaId).ToList();
                foreach (var link in links) cert.CompetenciaCertificacoes.Remove(link);
            }

            foreach (var port in prestador.Portfolio)
            {
                var links = port.CompetenciaPortfolios.Where(x => x.CompetenciaId == competenciaId).ToList();
                foreach (var link in links) port.CompetenciaPortfolios.Remove(link);
            }

            prestador.Competencias.Remove(comp);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(prestadorId);
        }
    }
}
