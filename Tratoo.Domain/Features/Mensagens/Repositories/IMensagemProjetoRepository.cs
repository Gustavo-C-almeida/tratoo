using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public interface IMensagemProjetoRepository
    {
        /// <summary>Lista mensagens de um par específico (contratante+prestador) em ordem cronológica.</summary>
        Task<List<MensagemProjeto>> GetDoPorAsync(int projetoId, int prestadorId, int pagina, int tamanhoPagina = 30);
        Task<int> ContarPorAsync(int projetoId, int prestadorId);
        /// <summary>Retorna resumo dos chats ativos de um prestador (um por projeto).</summary>
        Task<List<MensagemProjeto>> GetUltimasMensagensPorPrestadorAsync(int prestadorId);
        /// <summary>Retorna resumo dos chats ativos de um contratante (um por prestador por projeto).</summary>
        Task<List<MensagemProjeto>> GetUltimasMensagensPorContratanteAsync(int contratanteId);
        Task AddAsync(MensagemProjeto mensagem);
        Task SaveChangesAsync();
    }
}
