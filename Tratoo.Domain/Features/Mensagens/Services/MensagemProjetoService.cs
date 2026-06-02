using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Mensagens
{
    public class MensagemProjetoService
    {
        private readonly IMensagemProjetoRepository _repo;
        private readonly IProjetoRepository _projetoRepo;
        private readonly IUsuarioRepository _usuarioRepo;
        private readonly IPrestadorRepository _prestadorRepo;

        public MensagemProjetoService(
            IMensagemProjetoRepository repo,
            IProjetoRepository projetoRepo,
            IUsuarioRepository usuarioRepo,
            IPrestadorRepository prestadorRepo)
        {
            _repo = repo;
            _projetoRepo = projetoRepo;
            _usuarioRepo = usuarioRepo;
            _prestadorRepo = prestadorRepo;
        }

        /// <summary>
        /// Envia uma mensagem isolada por par (contratante + prestador).
        /// PrestadorId identifica a conversa — garante que prestadores diferentes
        /// não enxergam as mensagens uns dos outros.
        /// </summary>
        public async Task<MensagemProjetoDTO> EnviarAsync(EnviarMensagemProjetoDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Texto) || dto.Texto.Length > 2000)
                throw new NegocioException("A mensagem deve ter entre 1 e 2000 caracteres.");

            var projeto = await _projetoRepo.GetByIdAsync(dto.ProjetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            // Melhoria 5: EnviadoEm sempre UTC puro — System.Text.Json serializa com "Z"
            var mensagem = new MensagemProjeto
            {
                ProjetoId     = dto.ProjetoId,
                PrestadorId   = dto.PrestadorId,
                RemetenteId   = dto.RemetenteId,
                Texto         = dto.Texto.Trim(),
                Tipo          = dto.Tipo,
                PropostaVersaoId = dto.PropostaVersaoId,
                EnviadoEm     = DateTime.UtcNow
            };

            await _repo.AddAsync(mensagem);
            await _repo.SaveChangesAsync();

            var remetente = await _usuarioRepo.ObterPorIdAsync(dto.RemetenteId);

            return new MensagemProjetoDTO
            {
                Id             = mensagem.Id,
                ProjetoId      = mensagem.ProjetoId,
                PrestadorId    = mensagem.PrestadorId,
                RemetenteId    = mensagem.RemetenteId,
                RemetenteNome  = remetente?.Nome ?? string.Empty,
                RemetenteFotoUrl = null,
                Texto          = mensagem.Texto,
                Tipo           = mensagem.Tipo,
                PropostaVersaoId = mensagem.PropostaVersaoId,
                EnviadoEm      = DateTime.SpecifyKind(mensagem.EnviadoEm, DateTimeKind.Utc)
            };
        }

        /// <summary>
        /// Lista mensagens do par (projetoId + prestadorId) em ordem cronológica (mais antigas primeiro).
        /// </summary>
        public async Task<(List<MensagemProjetoDTO> Itens, int Total)> ListarAsync(
            int projetoId, int prestadorId, int pagina, int tamanhoPagina = 30)
        {
            var mensagens = await _repo.GetDoPorAsync(projetoId, prestadorId, pagina, tamanhoPagina);
            var total = await _repo.ContarPorAsync(projetoId, prestadorId);

            // Carrega nomes em batch
            var idsRemetentes = mensagens.Select(m => m.RemetenteId).Distinct().ToList();
            var nomes = new Dictionary<int, string>();
            foreach (var id in idsRemetentes)
            {
                var u = await _usuarioRepo.ObterPorIdAsync(id);
                if (u != null) nomes[id] = u.Nome;
            }

            var dtos = mensagens.Select(m => new MensagemProjetoDTO
            {
                Id             = m.Id,
                ProjetoId      = m.ProjetoId,
                PrestadorId    = m.PrestadorId,
                RemetenteId    = m.RemetenteId,
                RemetenteNome  = m.Remetente?.Nome ?? nomes.GetValueOrDefault(m.RemetenteId, ""),
                RemetenteFotoUrl = null,
                Texto          = m.Texto,
                Tipo           = m.Tipo,
                PropostaVersaoId = m.PropostaVersaoId,
                // Garante Kind=Utc para serialização com "Z"
                EnviadoEm      = DateTime.SpecifyKind(m.EnviadoEm, DateTimeKind.Utc)
            }).ToList();

            return (dtos, total);
        }

        /// <summary>
        /// Retorna lista de chats ativos para um prestador (um por projeto).
        /// Usado na página centralizada de chats.
        /// </summary>
        public async Task<List<ChatResumoDTO>> ListarChatsPrestadorAsync(int prestadorId)
        {
            var ultimas = await _repo.GetUltimasMensagensPorPrestadorAsync(prestadorId);

            var result = new List<ChatResumoDTO>();
            foreach (var m in ultimas.OrderByDescending(m => m.EnviadoEm))
            {
                var total = await _repo.ContarPorAsync(m.ProjetoId, prestadorId);
                var prestador = await _prestadorRepo.GetByIdAsync(prestadorId);
                result.Add(new ChatResumoDTO
                {
                    ProjetoId       = m.ProjetoId,
                    ProjetoTitulo   = m.Projeto?.Titulo ?? string.Empty,
                    PrestadorId     = prestadorId,
                    PrestadorNome   = prestador?.Nome ?? string.Empty,
                    UltimaMensagem  = m.Texto.Length > 80 ? m.Texto[..80] + "..." : m.Texto,
                    UltimaMensagemEm = DateTime.SpecifyKind(m.EnviadoEm, DateTimeKind.Utc),
                    TotalMensagens  = total
                });
            }
            return result;
        }

        /// <summary>
        /// Retorna lista de chats de um contratante — um item por par (projeto + prestador).
        /// </summary>
        public async Task<List<ChatResumoDTO>> ListarChatsContratanteAsync(int contratanteId)
        {
            var ultimas = await _repo.GetUltimasMensagensPorContratanteAsync(contratanteId);

            var result = new List<ChatResumoDTO>();
            foreach (var m in ultimas.OrderByDescending(m => m.EnviadoEm))
            {
                if (m.PrestadorId == null) continue;
                var total = await _repo.ContarPorAsync(m.ProjetoId, m.PrestadorId.Value);
                var prestador = await _prestadorRepo.GetByIdAsync(m.PrestadorId.Value);
                result.Add(new ChatResumoDTO
                {
                    ProjetoId       = m.ProjetoId,
                    ProjetoTitulo   = m.Projeto?.Titulo ?? string.Empty,
                    PrestadorId     = m.PrestadorId.Value,
                    PrestadorNome   = prestador?.Nome ?? string.Empty,
                    UltimaMensagem  = m.Texto.Length > 80 ? m.Texto[..80] + "..." : m.Texto,
                    UltimaMensagemEm = DateTime.SpecifyKind(m.EnviadoEm, DateTimeKind.Utc),
                    TotalMensagens  = total
                });
            }
            return result;
        }
    }
}
