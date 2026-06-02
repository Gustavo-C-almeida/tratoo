using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public class ChatConviteRepository : IChatConviteRepository
    {
        private readonly TratooContext _ctx;

        public ChatConviteRepository(TratooContext ctx) => _ctx = ctx;

        public async Task<ChatConvite?> GetByIdAsync(Guid id)
            => await _ctx.ChatsConvite
                .Include(c => c.Projeto)
                .Include(c => c.Contratante)
                .Include(c => c.Prestador)
                .Include(c => c.Convite)
                .FirstOrDefaultAsync(c => c.Id == id);

        public async Task<ChatConvite?> GetByConviteIdAsync(Guid conviteId)
            => await _ctx.ChatsConvite
                .Include(c => c.Projeto)
                .Include(c => c.Contratante)
                .Include(c => c.Prestador)
                .Include(c => c.Convite)
                .FirstOrDefaultAsync(c => c.ConviteId == conviteId);

        public async Task<List<ChatConvite>> GetChatsDoUsuarioAsync(int usuarioId)
            => await _ctx.ChatsConvite
                .Include(c => c.Projeto)
                .Include(c => c.Contratante)
                .Include(c => c.Prestador)
                .Include(c => c.Convite)
                .Where(c => c.ContratanteId == usuarioId || c.PrestadorId == usuarioId)
                .OrderByDescending(c => c.CriadoEm)
                .ToListAsync();

        public async Task<List<ChatConviteMensagem>> GetMensagensAsync(
            Guid chatId, int pagina, int tamanhoPagina)
            => await _ctx.ChatConviteMensagens
                .Include(m => m.Remetente)
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.EnviadoEm)
                .Skip((pagina - 1) * tamanhoPagina)
                .Take(tamanhoPagina)
                .ToListAsync();

        public async Task<int> ContarMensagensAsync(Guid chatId)
            => await _ctx.ChatConviteMensagens
                .CountAsync(m => m.ChatId == chatId);

        public async Task AddAsync(ChatConvite chat)
            => await _ctx.ChatsConvite.AddAsync(chat);

        public async Task AddMensagemAsync(ChatConviteMensagem mensagem)
            => await _ctx.ChatConviteMensagens.AddAsync(mensagem);

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
