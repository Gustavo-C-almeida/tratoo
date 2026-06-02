using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Propostas
{
    public class PropostaProjetoService
    {
        private readonly IPropostaProjetoRepository _repo;
        private readonly IProjetoRepository _projetoRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IUsuarioRepository _usuarioRepo;
        private readonly IEmailService _emailService;
        private readonly IContratoServicoService _contratoService;
        private readonly IConviteProjetoRepository _conviteRepo;
        private readonly IChatConviteRepository _chatRepo;

        public PropostaProjetoService(
            IPropostaProjetoRepository repo,
            IProjetoRepository projetoRepo,
            IPrestadorRepository prestadorRepo,
            IUsuarioRepository usuarioRepo,
            IEmailService emailService,
            IContratoServicoService contratoService,
            IConviteProjetoRepository conviteRepo,
            IChatConviteRepository chatRepo)
        {
            _repo = repo;
            _projetoRepo = projetoRepo;
            _prestadorRepo = prestadorRepo;
            _usuarioRepo = usuarioRepo;
            _emailService = emailService;
            _contratoService = contratoService;
            _conviteRepo = conviteRepo;
            _chatRepo = chatRepo;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CRIAR RASCUNHO (DRAFT)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> CriarRascunhoAsync(CriarRascunhoPropostaDTO dto)
        {
            var projeto = await _projetoRepo.GetByIdAsync(dto.ProjetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            if (projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("Este projeto não está aceitando propostas no momento.");

            if (projeto.ContratanteId == dto.PrestadorId)
                throw new NegocioException("Você não pode enviar proposta para um projeto que você criou.");

            var existente = await _repo.GetAtivaByPrestadorEProjetoAsync(dto.PrestadorId, dto.ProjetoId);
            if (existente != null)
                throw new NegocioException("Você já possui uma proposta ativa neste projeto.");

            if (dto.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Informe a validade da proposta (deve ser data futura).");

            var proposta = new PropostaProjeto
            {
                ProjetoId = dto.ProjetoId,
                PrestadorId = dto.PrestadorId,
                Status = StatusPropostaProjeto.Draft,
                VersaoAtual = 1,
                ValidoAte = dto.ValidoAte,
                CriadoEm = DateTime.UtcNow,
                AtualizadoEm = DateTime.UtcNow
            };

            await _repo.AddAsync(proposta);

            var versao = CriarVersaoEntity(proposta.Id, 1, dto.PrestadorId, dto);
            ValidarCamposVersao(versao);
            await _repo.AddVersaoAsync(versao);

            await _repo.SaveChangesAsync();

            proposta.Projeto = projeto;
            proposta.Versoes = new List<PropostaVersao> { versao };

            var prestador = await _prestadorRepo.GetByIdAsync(dto.PrestadorId);
            return MapDetalhe(proposta, prestador?.Nome ?? string.Empty, prestador?.FotoUrl, prestador?.TituloProfissional);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CRIAR PROPOSTA DO CONTRATANTE (fluxo reverso pós-convite)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> CriarPropostaContratanteAsync(
            Guid conviteId, int contratanteId, CriarRascunhoPropostaDTO dto)
        {
            var convite = await _conviteRepo.GetByIdAsync(conviteId)
                ?? throw new NegocioException("Convite não encontrado.");

            if (convite.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante dono do convite pode criar propostas.");

            if (convite.Status != StatusConvite.Aceito)
                throw new NegocioException("Só é possível criar proposta para convites aceitos.");

            var projeto = await _projetoRepo.GetByIdAsync(convite.ProjetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            if (projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("Este projeto não está mais aberto.");

            if (dto.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Informe a validade da proposta (deve ser data futura).");

            // Não permite duplicar proposta ativa do mesmo convite
            var propostaAtiva = await _repo.GetAtivaByConviteIdAsync(conviteId);
            if (propostaAtiva != null)
                throw new NegocioException("Já existe uma proposta ativa para este convite.");

            // Usa PrestadorId do convite como alvo
            dto.ProjetoId = convite.ProjetoId;
            dto.PrestadorId = convite.PrestadorId;

            var proposta = new PropostaProjeto
            {
                ProjetoId = convite.ProjetoId,
                PrestadorId = convite.PrestadorId,
                SenderType = PropostaSenderType.Contratante,
                ConviteId = conviteId,
                Status = StatusPropostaProjeto.Submitted, // já enviada diretamente
                VersaoAtual = 1,
                ValidoAte = dto.ValidoAte,
                CriadoEm = DateTime.UtcNow,
                AtualizadoEm = DateTime.UtcNow
            };

            await _repo.AddAsync(proposta);

            var versao = CriarVersaoEntity(proposta.Id, 1, contratanteId, dto);
            ValidarCamposVersao(versao);
            await _repo.AddVersaoAsync(versao);

            // Incrementa proposta no projeto
            projeto.TotalPropostas++;
            projeto.AtualizadoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            // Notifica prestador
            try
            {
                var prestador = await _prestadorRepo.GetByIdAsync(convite.PrestadorId);
                if (prestador != null)
                    await _emailService.EnviarPropostaContratanteAsync(
                        prestador.Email, prestador.Nome, projeto.Titulo);
            }
            catch { }

            proposta.Projeto = projeto;
            proposta.Versoes = new List<PropostaVersao> { versao };

            // Resolve ChatId para incluir no DTO
            var chat = await _chatRepo.GetByConviteIdAsync(conviteId);
            var prestadorInfo = await _prestadorRepo.GetByIdAsync(convite.PrestadorId);
            var detalhe = MapDetalhe(proposta, prestadorInfo?.Nome ?? string.Empty,
                prestadorInfo?.FotoUrl, prestadorInfo?.TituloProfissional);
            detalhe.ChatId = chat?.Id;
            return detalhe;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ACEITAR PELO PRESTADOR (fluxo reverso — Contratante enviou proposta)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> AceitarPorPrestadorAsync(Guid propostaId, int prestadorId)
        {
            var proposta = await _repo.GetByIdComVersoesAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            if (proposta.SenderType != PropostaSenderType.Contratante)
                throw new NegocioException("Este endpoint é exclusivo para propostas enviadas pelo contratante.");

            if (proposta.PrestadorId != prestadorId)
                throw new NegocioException("Apenas o prestador destinatário pode aceitar esta proposta.");

            var statusPermitidos = new[] { StatusPropostaProjeto.Submitted, StatusPropostaProjeto.EmNegociacao };
            if (!statusPermitidos.Contains(proposta.Status))
                throw new NegocioException("Proposta não pode ser aceita neste status.");

            if (proposta.Projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("O projeto não está mais aberto para aceite.");

            if (proposta.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Esta proposta já expirou.");

            var versaoAtiva = proposta.Versoes.OrderByDescending(v => v.Versao).FirstOrDefault()
                ?? throw new NegocioException("Proposta sem conteúdo — não é possível gerar contrato.");

            proposta.Status = StatusPropostaProjeto.Aceita;
            proposta.AtualizadoEm = DateTime.UtcNow;

            // Projeto em andamento com prestador selecionado
            proposta.Projeto.Status = StatusProjeto.EmAndamento;
            proposta.Projeto.FreelancerSelecionadoId = prestadorId;
            proposta.Projeto.AtualizadoEm = DateTime.UtcNow;

            // Recusa outras propostas ativas do projeto
            var outrasPropostas = await _repo.GetDoProjetoAsync(proposta.ProjetoId);
            foreach (var outra in outrasPropostas.Where(p =>
                p.Id != proposta.Id &&
                (p.Status == StatusPropostaProjeto.Submitted || p.Status == StatusPropostaProjeto.EmNegociacao)))
            {
                outra.Status = StatusPropostaProjeto.Recusada;
                outra.MotivoCancelamento = "Outro prestador foi selecionado.";
                outra.AtualizadoEm = DateTime.UtcNow;
            }

            await _repo.SaveChangesAsync();

            // Gera contrato
            await _contratoService.GerarAsync(proposta, versaoAtiva);

            proposta.Status = StatusPropostaProjeto.Convertida;
            proposta.AtualizadoEm = DateTime.UtcNow;
            await _repo.SaveChangesAsync();

            // Notifica contratante
            try
            {
                var contratante = await _usuarioRepo.ObterPorIdAsync(proposta.Projeto.ContratanteId);
                if (contratante != null)
                    await _emailService.EnviarPropostaAceitaPrestadorAsync(
                        contratante.Email, contratante.Nome, proposta.Projeto.Titulo);
            }
            catch { }

            var prestador = await _prestadorRepo.GetByIdAsync(prestadorId);
            return MapDetalhe(proposta, prestador?.Nome ?? string.Empty, prestador?.FotoUrl, prestador?.TituloProfissional);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ACEITAR PELA ÚLTIMA VERSÃO (prestador aceita contraproposta do contratante
        // no fluxo tradicional — quando a última versão foi criada pelo contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> AceitarUltimaVersaoAsync(Guid propostaId, int prestadorId)
        {
            var proposta = await _repo.GetByIdComVersoesAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            if (proposta.PrestadorId != prestadorId)
                throw new NegocioException("Apenas o prestador desta proposta pode aceitar.");

            var statusPermitidos = new[] { StatusPropostaProjeto.Submitted, StatusPropostaProjeto.EmNegociacao };
            if (!statusPermitidos.Contains(proposta.Status))
                throw new NegocioException("Proposta não pode ser aceita neste status.");

            if (proposta.Projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("O projeto não está mais aberto para aceite.");

            if (proposta.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Esta proposta já expirou.");

            var versaoAtiva = proposta.Versoes.OrderByDescending(v => v.Versao).FirstOrDefault()
                ?? throw new NegocioException("Proposta sem conteúdo — não é possível gerar contrato.");

            // Segurança: o prestador só pode aceitar quando a última versão foi criada pelo contratante
            var contratanteId = proposta.Projeto.ContratanteId;
            if (versaoAtiva.CriadoPor != contratanteId)
                throw new NegocioException("Você só pode aceitar quando a última versão foi enviada pelo contratante.");

            proposta.Status = StatusPropostaProjeto.Aceita;
            proposta.AtualizadoEm = DateTime.UtcNow;

            proposta.Projeto.Status = StatusProjeto.EmAndamento;
            proposta.Projeto.FreelancerSelecionadoId = prestadorId;
            proposta.Projeto.AtualizadoEm = DateTime.UtcNow;

            // Recusa outras propostas ativas do projeto
            var outrasPropostas = await _repo.GetDoProjetoAsync(proposta.ProjetoId);
            foreach (var outra in outrasPropostas.Where(p =>
                p.Id != proposta.Id &&
                (p.Status == StatusPropostaProjeto.Submitted || p.Status == StatusPropostaProjeto.EmNegociacao)))
            {
                outra.Status = StatusPropostaProjeto.Recusada;
                outra.MotivoCancelamento = "Outro prestador foi selecionado.";
                outra.AtualizadoEm = DateTime.UtcNow;
            }

            await _repo.SaveChangesAsync();

            await _contratoService.GerarAsync(proposta, versaoAtiva);

            proposta.Status = StatusPropostaProjeto.Convertida;
            proposta.AtualizadoEm = DateTime.UtcNow;
            await _repo.SaveChangesAsync();

            // Notifica contratante
            try
            {
                var contratante = await _usuarioRepo.ObterPorIdAsync(contratanteId);
                if (contratante != null)
                    await _emailService.EnviarNotificacaoAceiteAsync(
                        contratante.Email, contratante.Nome, proposta.Projeto.Titulo);
            }
            catch { }

            var prestador = await _prestadorRepo.GetByIdAsync(prestadorId);
            return MapDetalhe(proposta, prestador?.Nome ?? string.Empty, prestador?.FotoUrl, prestador?.TituloProfissional);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ENVIAR (DRAFT → SUBMITTED)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> EnviarAsync(Guid propostaId, int prestadorId)
        {
            var proposta = await _repo.GetByIdComVersoesAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            if (proposta.PrestadorId != prestadorId)
                throw new NegocioException("Você não tem permissão para enviar esta proposta.");

            if (proposta.Status != StatusPropostaProjeto.Draft)
                throw new NegocioException("Apenas rascunhos podem ser enviados.");

            if (proposta.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Informe a validade da proposta (deve ser data futura).");

            var versao = proposta.Versoes.OrderByDescending(v => v.Versao).FirstOrDefault()
                ?? throw new NegocioException("Proposta sem conteúdo. Preencha o formulário antes de enviar.");

            ValidarCamposVersao(versao);

            proposta.Status = StatusPropostaProjeto.Submitted;
            proposta.AtualizadoEm = DateTime.UtcNow;

            // Incrementa o contador de propostas no projeto
            proposta.Projeto.TotalPropostas++;
            proposta.Projeto.AtualizadoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            // Notifica conforme quem criou a proposta
            try
            {
                if (proposta.SenderType == PropostaSenderType.Contratante)
                {
                    // Fluxo reverso: notifica o prestador destinatário
                    var prest = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
                    if (prest != null)
                        await _emailService.EnviarPropostaContratanteAsync(
                            prest.Email, prest.Nome, proposta.Projeto.Titulo);
                }
                else
                {
                    await _emailService.EnviarNotificacaoPropostaEnviadaAsync(
                        proposta.Projeto.Contratante.Email,
                        proposta.Projeto.Contratante.Nome,
                        proposta.Projeto.Titulo);
                }
            }
            catch { }

            var prestador = await _prestadorRepo.GetByIdAsync(prestadorId);
            return MapDetalhe(proposta, prestador?.Nome ?? string.Empty, prestador?.FotoUrl, prestador?.TituloProfissional);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CONTRAPROPOSTA (cria nova PropostaVersao, incrementa VersaoAtual)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> ContrapropostaAsync(ContrapropostaDTO dto)
        {
            var proposta = await _repo.GetByIdComVersoesAsync(dto.PropostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            var projeto = proposta.Projeto;
            var ehParte = proposta.PrestadorId == dto.UsuarioId || projeto.ContratanteId == dto.UsuarioId;
            if (!ehParte)
                throw new NegocioException("Você não tem permissão para negociar esta proposta.");

            var statusPermitidos = new[] { StatusPropostaProjeto.Submitted, StatusPropostaProjeto.EmNegociacao };
            if (!statusPermitidos.Contains(proposta.Status))
                throw new NegocioException("Não é possível negociar neste status.");

            if (proposta.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Esta proposta já expirou. O prestador precisa renovar a validade.");

            if (proposta.VersaoAtual >= 10)
                throw new NegocioException("Limite de 10 versões de negociação atingido.");

            ValidarCamposContrapropostaDTO(dto);

            proposta.VersaoAtual++;
            proposta.Status = StatusPropostaProjeto.EmNegociacao;
            proposta.AtualizadoEm = DateTime.UtcNow;

            var novaVersao = new PropostaVersao
            {
                PropostaId = proposta.Id,
                Versao = proposta.VersaoAtual,
                CriadoPor = dto.UsuarioId,
                Objetivo = dto.Objetivo.Trim(),
                Escopo = dto.Escopo.Trim(),
                Exclusoes = dto.Exclusoes?.Trim(),
                RevisoesInclusas = dto.RevisoesInclusas,
                PrazoTotal = dto.PrazoTotal,
                ValorTotal = dto.ValorTotal,
                Entrada = dto.Entrada,
                FormaPagamento = dto.FormaPagamento,
                Observacoes = dto.Observacoes?.Trim(),
                MarcosJson = dto.MarcosJson,
                CriadoEm = DateTime.UtcNow
            };

            await _repo.AddVersaoAsync(novaVersao);
            await _repo.SaveChangesAsync();

            // Notifica a outra parte
            try
            {
                var outraParte = dto.UsuarioId == proposta.PrestadorId
                    ? await _usuarioRepo.ObterPorIdAsync(projeto.ContratanteId)
                    : await _usuarioRepo.ObterPorIdAsync(proposta.PrestadorId);

                if (outraParte != null)
                    await _emailService.EnviarNotificacaoContrapropostaAsync(
                        outraParte.Email, outraParte.Nome, projeto.Titulo);
            }
            catch { }

            var prestador = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
            return MapDetalhe(proposta, prestador?.Nome ?? string.Empty, prestador?.FotoUrl, prestador?.TituloProfissional);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ACEITAR (apenas contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO> AceitarAsync(Guid propostaId, int contratanteId)
        {
            var proposta = await _repo.GetByIdComVersoesAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            if (proposta.Projeto.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante do projeto pode aceitar propostas.");

            var statusPermitidos = new[] { StatusPropostaProjeto.Submitted, StatusPropostaProjeto.EmNegociacao };
            if (!statusPermitidos.Contains(proposta.Status))
                throw new NegocioException("Proposta não pode ser aceita neste status.");

            if (proposta.Projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("O projeto não está mais aberto para aceite.");

            if (proposta.ValidoAte.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("Esta proposta já expirou e não pode ser aceita.");

            var versaoAtiva = proposta.Versoes.OrderByDescending(v => v.Versao).FirstOrDefault()
                ?? throw new NegocioException("Proposta sem conteúdo — não é possível gerar contrato.");

            proposta.Status = StatusPropostaProjeto.Aceita;
            proposta.AtualizadoEm = DateTime.UtcNow;

            // Muda o projeto para EmAndamento e registra freelancer selecionado
            proposta.Projeto.Status = StatusProjeto.EmAndamento;
            proposta.Projeto.FreelancerSelecionadoId = proposta.PrestadorId;
            proposta.Projeto.AtualizadoEm = DateTime.UtcNow;

            // Recusa todas as outras propostas ativas do projeto
            var outrasPropostas = await _repo.GetDoProjetoAsync(proposta.ProjetoId);
            foreach (var outra in outrasPropostas.Where(p =>
                p.Id != proposta.Id &&
                (p.Status == StatusPropostaProjeto.Submitted || p.Status == StatusPropostaProjeto.EmNegociacao)))
            {
                outra.Status = StatusPropostaProjeto.Recusada;
                outra.MotivoCancelamento = "Outro prestador foi selecionado.";
                outra.AtualizadoEm = DateTime.UtcNow;
            }

            await _repo.SaveChangesAsync();

            // Gera contrato e marca proposta como Convertida
            await _contratoService.GerarAsync(proposta, versaoAtiva);

            proposta.Status = StatusPropostaProjeto.Convertida;
            proposta.AtualizadoEm = DateTime.UtcNow;
            await _repo.SaveChangesAsync();

            // Notifica o prestador
            try
            {
                var prestador = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
                if (prestador != null)
                    await _emailService.EnviarNotificacaoAceiteAsync(
                        prestador.Email, prestador.Nome, proposta.Projeto.Titulo);
            }
            catch { }

            var p = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
            return MapDetalhe(proposta, p?.Nome ?? string.Empty, p?.FotoUrl, p?.TituloProfissional);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RECUSAR (apenas contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task RecusarAsync(Guid propostaId, int contratanteId, string? motivo)
        {
            var proposta = await _repo.GetByIdAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            if (proposta.Projeto.ContratanteId != contratanteId)
                throw new NegocioException("Apenas o contratante pode recusar propostas.");

            var statusPermitidos = new[] { StatusPropostaProjeto.Submitted, StatusPropostaProjeto.EmNegociacao };
            if (!statusPermitidos.Contains(proposta.Status))
                throw new NegocioException("Esta proposta não pode ser recusada no status atual.");

            proposta.Status = StatusPropostaProjeto.Recusada;
            proposta.MotivoCancelamento = motivo?.Trim();
            proposta.CanceladoPorId = contratanteId;
            proposta.CanceladoEm = DateTime.UtcNow;
            proposta.AtualizadoEm = DateTime.UtcNow;

            // Decrementa o contador do projeto
            if (proposta.Projeto.TotalPropostas > 0)
            {
                proposta.Projeto.TotalPropostas--;
                proposta.Projeto.AtualizadoEm = DateTime.UtcNow;
            }

            await _repo.SaveChangesAsync();

            // Notifica o prestador
            try
            {
                var prestador = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
                if (prestador != null)
                    await _emailService.EnviarNotificacaoRecusaAsync(
                        prestador.Email, prestador.Nome, proposta.Projeto.Titulo, motivo);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CANCELAR (prestador ou contratante)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task CancelarAsync(Guid propostaId, int usuarioId, string? motivo)
        {
            var proposta = await _repo.GetByIdAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            var ehPrestador   = proposta.PrestadorId == usuarioId;
            var ehContratante = proposta.Projeto.ContratanteId == usuarioId;

            if (!ehPrestador && !ehContratante)
                throw new NegocioException("Você não tem permissão para cancelar esta proposta.");

            // DRAFT: apenas o prestador pode cancelar silenciosamente
            if (proposta.Status == StatusPropostaProjeto.Draft && !ehPrestador)
                throw new NegocioException("Apenas o prestador pode cancelar um rascunho.");

            var statusPermitidos = new[] {
                StatusPropostaProjeto.Draft,
                StatusPropostaProjeto.Submitted,
                StatusPropostaProjeto.EmNegociacao
            };
            if (!statusPermitidos.Contains(proposta.Status))
                throw new NegocioException("Esta proposta não pode ser cancelada no status atual.");

            var statusOriginal = proposta.Status;

            if (statusOriginal != StatusPropostaProjeto.Draft && proposta.Projeto.TotalPropostas > 0)
            {
                proposta.Projeto.TotalPropostas--;
                proposta.Projeto.AtualizadoEm = DateTime.UtcNow;
            }

            var motivoPadrao = ehPrestador ? "Cancelada pelo prestador." : "Cancelada pelo contratante.";
            proposta.Status = StatusPropostaProjeto.Recusada;
            proposta.MotivoCancelamento = motivo?.Trim() ?? motivoPadrao;
            proposta.CanceladoPorId = usuarioId;
            proposta.CanceladoEm = DateTime.UtcNow;
            proposta.AtualizadoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            // Notifica a outra parte (exceto para DRAFT, que é silencioso)
            if (statusOriginal != StatusPropostaProjeto.Draft)
            {
                try
                {
                    if (ehPrestador)
                    {
                        // Notifica contratante
                        var contratante = await _usuarioRepo.ObterPorIdAsync(proposta.Projeto.ContratanteId);
                        if (contratante != null)
                            await _emailService.EnviarNotificacaoRecusaAsync(
                                contratante.Email, contratante.Nome, proposta.Projeto.Titulo,
                                proposta.MotivoCancelamento);
                    }
                    else
                    {
                        // Notifica prestador
                        var prestador = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
                        if (prestador != null)
                            await _emailService.EnviarNotificacaoRecusaAsync(
                                prestador.Email, prestador.Nome, proposta.Projeto.Titulo,
                                proposta.MotivoCancelamento);
                    }
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<PropostaDetalheDTO?> ObterDetalheAsync(Guid propostaId, int usuarioId)
        {
            var proposta = await _repo.GetByIdComVersoesAsync(propostaId);
            if (proposta == null) return null;

            var ehParte = proposta.PrestadorId == usuarioId ||
                          proposta.Projeto.ContratanteId == usuarioId;
            if (!ehParte)
                throw new NegocioException("Você não tem permissão para ver esta proposta.");

            var prestador = await _prestadorRepo.GetByIdAsync(proposta.PrestadorId);
            return MapDetalhe(proposta, prestador?.Nome ?? string.Empty, prestador?.FotoUrl, prestador?.TituloProfissional);
        }

        public async Task<List<PropostaResumoDTO>> ListarDoProjetoAsync(int projetoId, int contratanteId)
        {
            var projeto = await _projetoRepo.GetByIdAsync(projetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            if (projeto.ContratanteId != contratanteId)
                throw new NegocioException("Você não tem permissão para ver as propostas deste projeto.");

            var propostas = await _repo.GetDoProjetoAsync(projetoId);
            return propostas.Select(MapResumo).ToList();
        }

        public async Task<List<PropostaResumoDTO>> ListarDoPrestadorAsync(int prestadorId)
        {
            var propostas = await _repo.GetDoPrestadorAsync(prestadorId);
            return propostas.Select(MapResumo).ToList();
        }

        public async Task<List<PropostaVersaoDTO>> ListarVersoesAsync(Guid propostaId, int usuarioId)
        {
            var proposta = await _repo.GetByIdAsync(propostaId)
                ?? throw new NegocioException("Proposta não encontrada.");

            var ehParte = proposta.PrestadorId == usuarioId ||
                          proposta.Projeto.ContratanteId == usuarioId;
            if (!ehParte)
                throw new NegocioException("Você não tem permissão para ver as versões desta proposta.");

            var versoes = await _repo.GetTodasVersoesAsync(propostaId);
            var usuariosIds = versoes.Select(v => v.CriadoPor).Distinct().ToList();
            var usuarios = new Dictionary<int, string>();
            foreach (var uid in usuariosIds)
            {
                var u = await _usuarioRepo.ObterPorIdAsync(uid);
                if (u != null) usuarios[uid] = u.Nome;
            }

            return versoes.Select(v => MapVersao(v, usuarios.GetValueOrDefault(v.CriadoPor, "Desconhecido"))).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Expiração (chamado pelo BackgroundService)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ExpirarPropostasAsync()
        {
            var expiradas = await _repo.GetExpiradas(DateTime.UtcNow);
            foreach (var proposta in expiradas)
            {
                proposta.Status = StatusPropostaProjeto.Expirada;
                proposta.AtualizadoEm = DateTime.UtcNow;
            }

            if (expiradas.Any())
                await _repo.SaveChangesAsync();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers privados
        // ─────────────────────────────────────────────────────────────────────────
        private static void ValidarCamposVersao(PropostaVersao v)
        {
            if (v.ValorTotal <= 0)
                throw new NegocioException("Informe o valor total da proposta.");
            if (v.RevisoesInclusas <= 0)
                throw new NegocioException("Informe quantas revisões estão inclusas.");
            if (v.PrazoTotal.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("O prazo de entrega deve ser uma data futura.");
            if (string.IsNullOrWhiteSpace(v.Objetivo) || v.Objetivo.Length < 20)
                throw new NegocioException("Descreva o objetivo com pelo menos 20 caracteres.");
            if (string.IsNullOrWhiteSpace(v.Escopo) || v.Escopo.Length < 50)
                throw new NegocioException("Detalhe o escopo com pelo menos 50 caracteres.");
            if (v.Entrada.HasValue && v.Entrada.Value > v.ValorTotal)
                throw new NegocioException("O valor de entrada não pode ser maior que o valor total.");
        }

        private static void ValidarCamposContrapropostaDTO(ContrapropostaDTO dto)
        {
            if (dto.ValorTotal <= 0)
                throw new NegocioException("Informe o valor total da proposta.");
            if (dto.RevisoesInclusas <= 0)
                throw new NegocioException("Informe quantas revisões estão inclusas.");
            if (dto.PrazoTotal.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("O prazo de entrega deve ser uma data futura.");
            if (string.IsNullOrWhiteSpace(dto.Objetivo) || dto.Objetivo.Length < 20)
                throw new NegocioException("Descreva o objetivo com pelo menos 20 caracteres.");
            if (string.IsNullOrWhiteSpace(dto.Escopo) || dto.Escopo.Length < 50)
                throw new NegocioException("Detalhe o escopo com pelo menos 50 caracteres.");
            if (dto.Entrada.HasValue && dto.Entrada.Value > dto.ValorTotal)
                throw new NegocioException("O valor de entrada não pode ser maior que o valor total.");
        }

        private static PropostaVersao CriarVersaoEntity(Guid propostaId, int numeroVersao, int criadoPor, CriarRascunhoPropostaDTO dto)
            => new()
            {
                PropostaId = propostaId,
                Versao = numeroVersao,
                CriadoPor = criadoPor,
                Objetivo = dto.Objetivo?.Trim() ?? string.Empty,
                Escopo = dto.Escopo?.Trim() ?? string.Empty,
                Exclusoes = dto.Exclusoes?.Trim(),
                RevisoesInclusas = dto.RevisoesInclusas,
                PrazoTotal = dto.PrazoTotal,
                ValorTotal = dto.ValorTotal,
                Entrada = dto.Entrada,
                FormaPagamento = dto.FormaPagamento,
                Observacoes = dto.Observacoes?.Trim(),
                MarcosJson = dto.MarcosJson,
                CriadoEm = DateTime.UtcNow
            };

        private static PropostaVersaoDTO MapVersao(PropostaVersao v, string criadoPorNome) => new()
        {
            Id = v.Id,
            PropostaId = v.PropostaId,
            Versao = v.Versao,
            Objetivo = v.Objetivo,
            Escopo = v.Escopo,
            Exclusoes = v.Exclusoes,
            RevisoesInclusas = v.RevisoesInclusas,
            PrazoTotal = v.PrazoTotal,
            ValorTotal = v.ValorTotal,
            Entrada = v.Entrada,
            FormaPagamento = v.FormaPagamento,
            Observacoes = v.Observacoes,
            MarcosJson = v.MarcosJson,
            CriadoPor = v.CriadoPor,
            CriadoPorNome = criadoPorNome,
            CriadoEm = v.CriadoEm
        };

        private static PropostaDetalheDTO MapDetalhe(
            PropostaProjeto p, string prestadorNome, string? fotoUrl, string? tituloProfissional)
        {
            var versoesOrdenadas = p.Versoes.OrderBy(v => v.Versao).ToList();
            var versaoAtiva = versoesOrdenadas.LastOrDefault();

            return new PropostaDetalheDTO
            {
                Id = p.Id,
                ProjetoId = p.ProjetoId,
                ProjetoTitulo = p.Projeto?.Titulo ?? string.Empty,
                ContratanteId = p.Projeto?.ContratanteId ?? 0,
                PrestadorId = p.PrestadorId,
                PrestadorNome = prestadorNome,
                PrestadorFotoUrl = fotoUrl,
                PrestadorTitulo = tituloProfissional,
                SenderType = p.SenderType,
                ConviteId = p.ConviteId,
                Status = p.Status,
                VersaoAtual = p.VersaoAtual,
                ValidoAte = p.ValidoAte,
                MotivoCancelamento = p.MotivoCancelamento,
                CriadoEm = p.CriadoEm,
                AtualizadoEm = p.AtualizadoEm,
                VersaoAtiva = versaoAtiva != null
                    ? MapVersao(versaoAtiva, prestadorNome)
                    : null,
                Versoes = versoesOrdenadas.Select(v => MapVersao(v, prestadorNome)).ToList()
            };
        }

        private static PropostaResumoDTO MapResumo(PropostaProjeto p)
        {
            var versaoAtiva = p.Versoes.OrderByDescending(v => v.Versao).FirstOrDefault();
            return new PropostaResumoDTO
            {
                Id = p.Id,
                ProjetoId = p.ProjetoId,
                ProjetoTitulo = p.Projeto?.Titulo ?? string.Empty,
                PrestadorId = p.PrestadorId,
                PrestadorNome = p.Prestador?.Nome ?? string.Empty,
                PrestadorFotoUrl = p.Prestador?.FotoUrl,
                PrestadorTitulo = p.Prestador?.TituloProfissional,
                Status = p.Status,
                SenderType = p.SenderType,
                ConviteId = p.ConviteId,
                VersaoAtual = p.VersaoAtual,
                ValidoAte = p.ValidoAte,
                CriadoEm = p.CriadoEm,
                ValorTotal = versaoAtiva?.ValorTotal ?? 0,
                PrazoTotal = versaoAtiva?.PrazoTotal ?? DateTime.MinValue,
                Objetivo = versaoAtiva?.Objetivo ?? string.Empty,
                RevisoesInclusas = versaoAtiva?.RevisoesInclusas ?? 0
            };
        }
    }
}
