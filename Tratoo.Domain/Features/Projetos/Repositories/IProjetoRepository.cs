using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Projetos
{
    public interface IProjetoRepository
    {
        Task<Projeto?> GetByIdAsync(int id);

        /// <summary>
        /// O prestador tem vínculo com o projeto PRIVADO: convite pendente/aceito ou contrato no
        /// projeto. Proposta sozinha não conta (toda proposta legítima em privado tem convite).
        /// Decide quem, além do dono, do Admin e do selecionado, enxerga um projeto privado.
        /// </summary>
        Task<bool> PrestadorTemVinculoAsync(int projetoId, int prestadorId);

        /// <summary>
        /// O prestador tem convite vigente no projeto: aceito, ou pendente dentro do prazo e com o
        /// projeto Aberto (ver ConviteProjeto.PendenteVencido). É o único vínculo que autoriza
        /// CRIAR proposta em projeto privado (proposta anterior não conta: seria o próprio
        /// vínculo que se quer impedir de forjar).
        /// </summary>
        Task<bool> PrestadorConvidadoAsync(int projetoId, int prestadorId);
        Task<(List<Projeto> Itens, int Total)> BuscarAsync(FiltrosProjetoDTO filtros);
        Task<List<Projeto>> GetDoContratanteAsync(int contratanteId);
        Task AddAsync(Projeto projeto);
        Task SaveChangesAsync();
    }
}
