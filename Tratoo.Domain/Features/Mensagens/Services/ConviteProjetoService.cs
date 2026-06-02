using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public class ConviteProjetoService
    {
        private readonly IConviteProjetoRepository _repo;
        private readonly IProjetoRepository _projetoRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IEmailService _emailService;
        private readonly IChatConviteRepository _chatRepo;

        public ConviteProjetoService(
            IConviteProjetoRepository repo,
            IProjetoRepository projetoRepo,
            IPrestadorRepository prestadorRepo,
            IEmailService emailService,
            IChatConviteRepository chatRepo)
        {
            _repo = repo;
            _projetoRepo = projetoRepo;
            _prestadorRepo = prestadorRepo;
            _emailService = emailService;
            _chatRepo = chatRepo;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ENVIAR CONVITE (Contratante → Prestador)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ConviteProjetoDTO> EnviarAsync(CriarConviteProjetoDTO dto)
        {
            var projeto = await _projetoRepo.GetByIdAsync(dto.ProjetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            if (projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("Este projeto não está aceitando convites no momento.");

            if (projeto.ContratanteId != dto.ContratanteId)
                throw new NegocioException("Apenas o contratante dono do projeto pode enviar convites.");

            if (dto.PrestadorId == dto.ContratanteId)
                throw new NegocioException("Você não pode convidar a si mesmo.");

            var prestador = await _prestadorRepo.GetByIdAsync(dto.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado.");

            if (string.IsNullOrWhiteSpace(dto.MensagemInicial) || dto.MensagemInicial.Trim().Length < 10)
                throw new NegocioException("A mensagem inicial deve ter pelo menos 10 caracteres.");

            // Verifica se já existe convite pendente para este prestador neste projeto
            var existente = await _repo.GetAtivoByPrestadorEProjetoAsync(dto.PrestadorId, dto.ProjetoId);
            if (existente != null)
                throw new NegocioException("Já existe um convite pendente para este prestador neste projeto.");

            var convite = new ConviteProjeto
            {
                ProjetoId = dto.ProjetoId,
                ContratanteId = dto.ContratanteId,
                PrestadorId = dto.PrestadorId,
                MensagemInicial = dto.MensagemInicial.Trim(),
                OrcamentoSugerido = dto.OrcamentoSugerido,
                PrazoDesejado = dto.PrazoDesejado,
                Status = StatusConvite.Pendente,
                CriadoEm = DateTime.UtcNow
            };

            await _repo.AddAsync(convite);
            await _repo.SaveChangesAsync();

            // Notifica prestador por e-mail (silencioso)
            try
            {
                await _emailService.EnviarConviteProjetoAsync(
                    prestador.Email,
                    prestador.Nome,
                    projeto.Titulo,
                    projeto.Contratante?.Nome ?? "Contratante",
                    dto.MensagemInicial);
            }
            catch { }

            convite.Projeto = projeto;
            convite.Contratante = projeto.Contratante!;
            convite.Prestador = prestador;

            return MapDTO(convite);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ACEITAR CONVITE (Prestador)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ConviteProjetoDTO> AceitarAsync(Guid conviteId, int prestadorId)
        {
            var convite = await _repo.GetByIdAsync(conviteId)
                ?? throw new NegocioException("Convite não encontrado.");

            if (convite.PrestadorId != prestadorId)
                throw new NegocioException("Você não tem permissão para responder este convite.");

            if (convite.Status != StatusConvite.Pendente)
                throw new NegocioException("Este convite já foi respondido.");

            convite.Status = StatusConvite.Aceito;
            convite.RespondidoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            // Cria o chat de negociação contextualizado (1:1 com convite)
            var chatExistente = await _chatRepo.GetByConviteIdAsync(convite.Id);
            if (chatExistente == null)
            {
                var chat = new Models.ChatConvite
                {
                    ProjetoId = convite.ProjetoId,
                    ContratanteId = convite.ContratanteId,
                    PrestadorId = convite.PrestadorId,
                    ConviteId = convite.Id,
                    CriadoEm = DateTime.UtcNow
                };
                await _chatRepo.AddAsync(chat);
                await _chatRepo.SaveChangesAsync();
            }

            // Notifica contratante por e-mail (silencioso)
            try
            {
                await _emailService.EnviarConviteAceitoAsync(
                    convite.Contratante.Email,
                    convite.Contratante.Nome,
                    convite.Projeto.Titulo,
                    convite.Prestador.Nome);
            }
            catch { }

            return MapDTO(convite);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RECUSAR CONVITE (Prestador)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ConviteProjetoDTO> RecusarAsync(Guid conviteId, int prestadorId, string? motivo)
        {
            var convite = await _repo.GetByIdAsync(conviteId)
                ?? throw new NegocioException("Convite não encontrado.");

            if (convite.PrestadorId != prestadorId)
                throw new NegocioException("Você não tem permissão para responder este convite.");

            if (convite.Status != StatusConvite.Pendente)
                throw new NegocioException("Este convite já foi respondido.");

            convite.Status = StatusConvite.Recusado;
            convite.RespondidoEm = DateTime.UtcNow;
            convite.MotivoRecusa = motivo?.Trim();

            await _repo.SaveChangesAsync();

            // Notifica contratante por e-mail (silencioso)
            try
            {
                await _emailService.EnviarConviteRecusadoAsync(
                    convite.Contratante.Email,
                    convite.Contratante.Nome,
                    convite.Projeto.Titulo,
                    convite.Prestador.Nome,
                    motivo);
            }
            catch { }

            return MapDTO(convite);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER POR ID
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ConviteProjetoDTO?> ObterPorIdAsync(Guid conviteId, int usuarioId)
        {
            var convite = await _repo.GetByIdAsync(conviteId);
            if (convite == null) return null;

            if (convite.ContratanteId != usuarioId && convite.PrestadorId != usuarioId)
                throw new NegocioException("Acesso negado.");

            return MapDTO(convite);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LISTAR — convites recebidos pelo prestador
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<List<ConviteProjetoDTO>> ListarDoPrestadorAsync(int prestadorId)
        {
            var convites = await _repo.GetDoPrestadorAsync(prestadorId);
            return convites.Select(MapDTO).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LISTAR — convites enviados para um projeto (contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<List<ConviteProjetoDTO>> ListarDoProjetoAsync(int projetoId, int contratanteId)
        {
            var projeto = await _projetoRepo.GetByIdAsync(projetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            if (projeto.ContratanteId != contratanteId)
                throw new NegocioException("Acesso negado.");

            var convites = await _repo.GetDoProjetoAsync(projetoId);
            return convites.Select(MapDTO).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // MAPPING
        // ─────────────────────────────────────────────────────────────────────────
        private static ConviteProjetoDTO MapDTO(ConviteProjeto c) => new()
        {
            Id = c.Id,
            ProjetoId = c.ProjetoId,
            ProjetoTitulo = c.Projeto?.Titulo ?? string.Empty,
            ProjetoStatus = c.Projeto?.Status ?? StatusProjeto.Aberto,
            OrcamentoMin = c.Projeto?.OrcamentoMin ?? 0,
            OrcamentoMax = c.Projeto?.OrcamentoMax ?? 0,
            ContratanteId = c.ContratanteId,
            ContratanteNome = c.Contratante?.Nome ?? string.Empty,
            PrestadorId = c.PrestadorId,
            PrestadorNome = c.Prestador?.Nome ?? string.Empty,
            MensagemInicial = c.MensagemInicial,
            OrcamentoSugerido = c.OrcamentoSugerido,
            PrazoDesejado = c.PrazoDesejado,
            Status = c.Status,
            CriadoEm = c.CriadoEm,
            RespondidoEm = c.RespondidoEm,
            MotivoRecusa = c.MotivoRecusa
        };
    }
}
