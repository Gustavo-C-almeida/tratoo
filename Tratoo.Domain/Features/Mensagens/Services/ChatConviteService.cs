using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public class ChatConviteService
    {
        private readonly IChatConviteRepository _repo;
        private readonly IUsuarioRepository _usuarioRepo;

        public ChatConviteService(
            IChatConviteRepository repo,
            IUsuarioRepository usuarioRepo)
        {
            _repo = repo;
            _usuarioRepo = usuarioRepo;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER CHAT DO CONVITE
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ChatConviteDTO> ObterPorConviteAsync(Guid conviteId, int usuarioId)
        {
            var chat = await _repo.GetByConviteIdAsync(conviteId)
                ?? throw new NegocioException("Chat não encontrado para este convite.");

            if (chat.ContratanteId != usuarioId && chat.PrestadorId != usuarioId)
                throw new NegocioException("Você não tem acesso a este chat.");

            var total = await _repo.ContarMensagensAsync(chat.Id);
            return MapDTO(chat, total);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER CHAT POR ID
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ChatConviteDTO> ObterPorIdAsync(Guid chatId, int usuarioId)
        {
            var chat = await _repo.GetByIdAsync(chatId)
                ?? throw new NegocioException("Chat não encontrado.");

            if (chat.ContratanteId != usuarioId && chat.PrestadorId != usuarioId)
                throw new NegocioException("Você não tem acesso a este chat.");

            var total = await _repo.ContarMensagensAsync(chatId);
            return MapDTO(chat, total);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LISTAR MEUS CHATS
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<List<ChatConviteDTO>> ListarMeusChatsAsync(int usuarioId)
        {
            var chats = await _repo.GetChatsDoUsuarioAsync(usuarioId);
            var result = new List<ChatConviteDTO>();
            foreach (var chat in chats)
            {
                var total = await _repo.ContarMensagensAsync(chat.Id);
                result.Add(MapDTO(chat, total));
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LISTAR MENSAGENS
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<(List<ChatConviteMensagemDTO> Itens, int Total)> ListarMensagensAsync(
            Guid chatId, int usuarioId, int pagina = 1, int tamanhoPagina = 50)
        {
            var chat = await _repo.GetByIdAsync(chatId)
                ?? throw new NegocioException("Chat não encontrado.");

            if (chat.ContratanteId != usuarioId && chat.PrestadorId != usuarioId)
                throw new NegocioException("Você não tem acesso a este chat.");

            var mensagens = await _repo.GetMensagensAsync(chatId, pagina, tamanhoPagina);
            var total = await _repo.ContarMensagensAsync(chatId);

            var dtos = mensagens.Select(m => new ChatConviteMensagemDTO
            {
                Id = m.Id,
                ChatId = m.ChatId,
                RemetenteId = m.RemetenteId,
                RemetenteNome = m.Remetente?.Nome ?? string.Empty,
                Texto = m.Texto,
                EnviadoEm = m.EnviadoEm
            }).ToList();

            return (dtos, total);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ENVIAR MENSAGEM
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ChatConviteMensagemDTO> EnviarMensagemAsync(EnviarChatMensagemDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Texto) || dto.Texto.Trim().Length < 1)
                throw new NegocioException("A mensagem não pode ser vazia.");

            if (dto.Texto.Length > 2000)
                throw new NegocioException("A mensagem deve ter no máximo 2000 caracteres.");

            var chat = await _repo.GetByIdAsync(dto.ChatId)
                ?? throw new NegocioException("Chat não encontrado.");

            if (chat.ContratanteId != dto.RemetenteId && chat.PrestadorId != dto.RemetenteId)
                throw new NegocioException("Você não tem permissão para enviar mensagens neste chat.");

            var mensagem = new ChatConviteMensagem
            {
                ChatId = dto.ChatId,
                RemetenteId = dto.RemetenteId,
                Texto = dto.Texto.Trim(),
                EnviadoEm = DateTime.UtcNow
            };

            await _repo.AddMensagemAsync(mensagem);
            await _repo.SaveChangesAsync();

            var remetente = await _usuarioRepo.ObterPorIdAsync(dto.RemetenteId);

            return new ChatConviteMensagemDTO
            {
                Id = mensagem.Id,
                ChatId = mensagem.ChatId,
                RemetenteId = mensagem.RemetenteId,
                RemetenteNome = remetente?.Nome ?? string.Empty,
                Texto = mensagem.Texto,
                EnviadoEm = mensagem.EnviadoEm
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // MAPPING
        // ─────────────────────────────────────────────────────────────────────────
        private static ChatConviteDTO MapDTO(ChatConvite c, int totalMensagens) => new()
        {
            Id = c.Id,
            ConviteId = c.ConviteId,
            ProjetoId = c.ProjetoId,
            ProjetoTitulo = c.Projeto?.Titulo ?? string.Empty,
            ContratanteId = c.ContratanteId,
            ContratanteNome = c.Contratante?.Nome ?? string.Empty,
            PrestadorId = c.PrestadorId,
            PrestadorNome = c.Prestador?.Nome ?? string.Empty,
            CriadoEm = c.CriadoEm,
            TotalMensagens = totalMensagens
        };
    }
}
