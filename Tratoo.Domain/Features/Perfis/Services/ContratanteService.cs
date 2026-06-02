using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    public interface IContratanteService
    {
        Task CompleteProfileAsync(CompleteProfileContratanteDTO dto);
    }

    public class ContratanteService : IContratanteService
    {
        private readonly IContratanteRepository _contratanteRepository;

        public ContratanteService(IContratanteRepository contratanteRepository)
        {
            _contratanteRepository = contratanteRepository;
        }

        public async Task CompleteProfileAsync(CompleteProfileContratanteDTO dto)
        {
            var contratante = await _contratanteRepository.GetByIdAsync(dto.ContratanteId)
                ?? throw new NegocioException("Contratante não encontrado");

            if (dto.TipoPessoa.HasValue)
                contratante.TipoPessoa = dto.TipoPessoa;

            contratante.Segmento = dto.Segmento;
            contratante.NomeEmpresa = dto.NomeEmpresa;

            contratante.Endereco ??= new Tratoo.Domain.Models.Endereco();
            contratante.Endereco.Cidade = dto.Cidade;
            contratante.Endereco.Estado = dto.Estado;

            contratante.VerificarPerfilMinimo();

            await _contratanteRepository.UpdateAsync(contratante);
            await _contratanteRepository.SaveAsync();
        }
    }
}
