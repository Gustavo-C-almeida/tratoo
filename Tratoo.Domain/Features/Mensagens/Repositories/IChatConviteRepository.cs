using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public interface IChatConviteRepository
    {
        Task<ChatConvite?> GetByIdAsync(Guid id);
        Task<ChatConvite?> GetByConviteIdAsync(Guid conviteId);
        Task<List<ChatConvite>> GetChatsDoUsuarioAsync(int usuarioId);
        Task<List<ChatConviteMensagem>> GetMensagensAsync(Guid chatId, int pagina, int tamanhoPagina);
        Task<int> ContarMensagensAsync(Guid chatId);
        Task AddAsync(ChatConvite chat);
        Task AddMensagemAsync(ChatConviteMensagem mensagem);
        Task SaveChangesAsync();
    }
}
