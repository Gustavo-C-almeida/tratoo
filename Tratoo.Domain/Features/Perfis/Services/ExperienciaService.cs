using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public class ExperienciaService
    {
        private readonly IPrestadorRepository _repo;
        private readonly PrestadorIndexadorService _indexador;

        public ExperienciaService(IPrestadorRepository repo, PrestadorIndexadorService indexador)
        {
            _repo = repo;
            _indexador = indexador;
        }

        public async Task AdicionarAsync(ExperienciaDTO dto)
        {
            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var exp = new ExperienciaPrestador
            {
                PrestadorId  = dto.PrestadorId,
                Empresa      = dto.Empresa,
                Cargo        = dto.Cargo,
                Atividades   = dto.Atividades,
                DataInicio   = dto.DataInicio,
                DataFim      = dto.DataFim,
                EmpregoAtual = dto.EmpregoAtual,
                Local        = dto.Local,
                TipoContrato = dto.TipoContrato
            };

            prestador.Experiencias.Add(exp);
            prestador.PorcentagemCompleto = PerfilProfissaoPrestadorService.CalcularCompletude(prestador);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task EditarAsync(ExperienciaDTO dto)
        {
            var prestador = await _repo.GetCompletoAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var exp = prestador.Experiencias.FirstOrDefault(e => e.Id == dto.Id)
                ?? throw new NegocioException("Experiência não encontrada");

            exp.Empresa      = dto.Empresa;
            exp.Cargo        = dto.Cargo;
            exp.Atividades   = dto.Atividades;
            exp.DataInicio   = dto.DataInicio;
            exp.DataFim      = dto.DataFim;
            exp.EmpregoAtual = dto.EmpregoAtual;
            exp.Local        = dto.Local;
            exp.TipoContrato = dto.TipoContrato;

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(dto.PrestadorId);
        }

        public async Task RemoverAsync(int experienciaId, int prestadorId)
        {
            var prestador = await _repo.GetCompletoAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado");

            var exp = prestador.Experiencias.FirstOrDefault(e => e.Id == experienciaId);
            if (exp == null) return;

            prestador.Experiencias.Remove(exp);
            prestador.PorcentagemCompleto = PerfilProfissaoPrestadorService.CalcularCompletude(prestador);

            await _repo.SaveAsync();
            await _indexador.IndexarAsync(prestadorId);
        }
    }
}
