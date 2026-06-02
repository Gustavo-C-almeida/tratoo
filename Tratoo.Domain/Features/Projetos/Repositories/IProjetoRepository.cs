using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Projetos
{
    public interface IProjetoRepository
    {
        Task<Projeto?> GetByIdAsync(int id);
        Task<(List<Projeto> Itens, int Total)> BuscarAsync(FiltrosProjetoDTO filtros);
        Task<List<Projeto>> GetDoContratanteAsync(int contratanteId);
        Task AddAsync(Projeto projeto);
        Task SaveChangesAsync();
    }
}
