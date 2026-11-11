using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Projetos
{
    public interface IProjetoRepository
    {
        Task<Projeto?> GetByIdAsync(int id);

        /// <summary>
        /// O prestador tem vínculo com o projeto: proposta (qualquer status) ou convite pendente/aceito.
        /// Decide quem, além do dono, enxerga um projeto privado.
        /// </summary>
        Task<bool> PrestadorParticipaAsync(int projetoId, int prestadorId);
        Task<(List<Projeto> Itens, int Total)> BuscarAsync(FiltrosProjetoDTO filtros);
        Task<List<Projeto>> GetDoContratanteAsync(int contratanteId);
        Task AddAsync(Projeto projeto);
        Task SaveChangesAsync();
    }
}
