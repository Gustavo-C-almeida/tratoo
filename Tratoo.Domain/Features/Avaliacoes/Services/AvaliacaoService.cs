using Microsoft.Extensions.Logging;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Avaliacoes
{
    public class AvaliacaoService : IAvaliacaoService
    {
        private const int DiasParaExpirar = 7;

        private readonly IAvaliacaoRepository _repo;
        private readonly IContratoServicoRepository _contratoRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IContratanteRepository _contratanteRepo;
        private readonly PrestadorIndexadorService _prestadorIndexador;
        private readonly IEmailService _emailService;
        private readonly ILogger<AvaliacaoService> _logger;

        public AvaliacaoService(
            IAvaliacaoRepository repo,
            IContratoServicoRepository contratoRepo,
            IPrestadorRepository prestadorRepo,
            IContratanteRepository contratanteRepo,
            PrestadorIndexadorService prestadorIndexador,
            IEmailService emailService,
            ILogger<AvaliacaoService> logger)
        {
            _repo = repo;
            _contratoRepo = contratoRepo;
            _prestadorRepo = prestadorRepo;
            _contratanteRepo = contratanteRepo;
            _prestadorIndexador = prestadorIndexador;
            _emailService = emailService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CRIAR SLOTS PENDENTES (idempotente)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task CriarAvaliacoesPendentesAsync(Guid contratoServicoId)
        {
            var contrato = await _contratoRepo.GetByIdAsync(contratoServicoId);
            if (contrato == null)
            {
                _logger.LogWarning("CriarAvaliacoesPendentes: contrato {Id} não encontrado.", contratoServicoId);
                return;
            }

            // Idempotência — não cria duplicatas
            var existentes = await _repo.GetDoContratoAsync(contratoServicoId);
            if (existentes.Count >= 2)
            {
                _logger.LogDebug("Avaliações para contrato {Id} já existem — ignorado.", contratoServicoId);
                return;
            }

            // Marca contrato como Encerrado (se ainda não estiver)
            if (contrato.Status == ContratoServicoStatus.Ativo)
            {
                contrato.Status = ContratoServicoStatus.Encerrado;
            }

            var agora = DateTime.UtcNow;

            var idsExistentes = existentes.Select(e => e.AvaliadorId).ToHashSet();

            // Slot 1: Contratante avalia Prestador
            if (!idsExistentes.Contains(contrato.ContratanteId))
            {
                await _repo.AddAsync(new Avaliacao
                {
                    Id = Guid.NewGuid(),
                    ContratoServicoId = contratoServicoId,
                    AvaliadorId = contrato.ContratanteId,
                    AvaliadoId = contrato.PrestadorId,
                    Status = StatusAvaliacao.Pendente,
                    CriadoEm = agora
                });
            }

            // Slot 2: Prestador avalia Contratante
            if (!idsExistentes.Contains(contrato.PrestadorId))
            {
                await _repo.AddAsync(new Avaliacao
                {
                    Id = Guid.NewGuid(),
                    ContratoServicoId = contratoServicoId,
                    AvaliadorId = contrato.PrestadorId,
                    AvaliadoId = contrato.ContratanteId,
                    Status = StatusAvaliacao.Pendente,
                    CriadoEm = agora
                });
            }

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Slots de avaliação criados para contrato {ContratoId} (contratante {C} ↔ prestador {P}).",
                contratoServicoId, contrato.ContratanteId, contrato.PrestadorId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ENVIAR AVALIAÇÃO (blind review)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task EnviarAvaliacaoAsync(Guid avaliacaoId, int avaliadorId, EnviarAvaliacaoDto dto)
        {
            var avaliacao = await _repo.GetByIdAsync(avaliacaoId)
                ?? throw new NegocioException("Avaliação não encontrada.");

            // Validações de domínio
            if (avaliacao.AvaliadorId != avaliadorId)
                throw new NegocioException("Você não é o avaliador desta avaliação.");

            if (avaliacao.Status != StatusAvaliacao.Pendente)
                throw new NegocioException("Esta avaliação já foi publicada ou não está disponível para envio.");

            if (avaliacao.Nota != null)
                throw new NegocioException("Você já enviou sua avaliação para este contrato.");

            if (dto.Nota < 1 || dto.Nota > 5)
                throw new NegocioException("A nota deve ser entre 1 e 5.");

            if (dto.Comentario != null && dto.Comentario.Length > 1000)
                throw new NegocioException("O comentário não pode ultrapassar 1000 caracteres.");

            if (avaliacao.AvaliadorId == avaliacao.AvaliadoId)
                throw new NegocioException("Avaliador e avaliado não podem ser o mesmo usuário.");

            // Monitoramento anti-fraude básico (sem bloqueio no MVP)
            var mutuais = await _repo.CountAvaliacoesMutuasAsync(avaliadorId, avaliacao.AvaliadoId);
            if (mutuais > 6) // > 3 contratos = > 6 slots de avaliação
            {
                _logger.LogWarning(
                    "[ANTIFRAUDE] Par suspeito: usuário {A} e {B} têm {N} avaliações mútuas.",
                    avaliadorId, avaliacao.AvaliadoId, mutuais);
            }

            // Salva a avaliação do avaliador
            avaliacao.Nota = dto.Nota;
            avaliacao.Comentario = dto.Comentario?.Trim();
            avaliacao.Publica = dto.Publica;

            // Verifica se o outro lado já avaliou (blind review)
            var todasDoContrato = await _repo.GetDoContratoAsync(avaliacao.ContratoServicoId);
            var outraAvaliacao = todasDoContrato.FirstOrDefault(a => a.Id != avaliacaoId);

            bool outroJaAvaliou = outraAvaliacao?.Nota != null;

            if (outroJaAvaliou)
            {
                // Ambos avaliaram — publica as duas simultaneamente
                PublicarAvaliacao(avaliacao);
                if (outraAvaliacao != null)
                    PublicarAvaliacao(outraAvaliacao);

                _logger.LogInformation(
                    "Blind review concluído para contrato {ContratoId}. Ambas as avaliações publicadas.",
                    avaliacao.ContratoServicoId);
            }

            await _repo.SaveChangesAsync();

            // Recalcula reputação e re-indexa embedding dos avaliados (após SaveChanges)
            if (outroJaAvaliou)
            {
                await RecalcularReputacaoAsync(avaliacao.AvaliadoId);
                await ReindexarSePrestadorAsync(avaliacao.AvaliadoId);

                if (outraAvaliacao != null)
                {
                    await RecalcularReputacaoAsync(outraAvaliacao.AvaliadoId);
                    await ReindexarSePrestadorAsync(outraAvaliacao.AvaliadoId);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CONSULTAS
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<AvaliacaoPendenteDto?> ObterPendenteDoContratoAsync(Guid contratoServicoId, int userId)
        {
            var av = await _repo.GetPendenteDoUsuarioNoContratoAsync(contratoServicoId, userId);
            if (av == null) return null;

            return MapearParaPendenteDto(av);
        }

        public async Task<List<AvaliacaoPendenteDto>> ListarPendentesAsync(int userId)
        {
            var pendentes = await _repo.GetPendentesPorAvaliadorAsync(userId);
            return pendentes.Select(MapearParaPendenteDto).ToList();
        }

        public async Task<ReputacaoDto> ObterReputacaoAsync(int avaliadoId)
        {
            var resumo = await _repo.GetReputacaoAsync(avaliadoId);

            // Determina se o avaliado permite exibição pública
            bool publica = await ResolverPrivacidadeAsync(avaliadoId);

            if (resumo == null || !publica)
            {
                return new ReputacaoDto
                {
                    UsuarioId = avaliadoId,
                    MediaGeral = 0,
                    TotalAvaliacoes = 0,
                    Distribuicao = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0 },
                    Publica = publica
                };
            }

            return new ReputacaoDto
            {
                UsuarioId = avaliadoId,
                MediaGeral = resumo.MediaGeral,
                TotalAvaliacoes = resumo.TotalAvaliacoes,
                Distribuicao = new Dictionary<int, int>
                {
                    [1] = resumo.Distribuicao1,
                    [2] = resumo.Distribuicao2,
                    [3] = resumo.Distribuicao3,
                    [4] = resumo.Distribuicao4,
                    [5] = resumo.Distribuicao5
                },
                Publica = true
            };
        }

        public async Task<AvaliacoesPublicasDto> ListarPublicasAsync(int avaliadoId, int pagina, int tamanhoPagina)
        {
            // RN-AV-003/004/011: validação de privacidade na camada de serviço — dados não chegam ao frontend
            bool publica = await ResolverPrivacidadeAsync(avaliadoId);
            if (!publica)
                return new AvaliacoesPublicasDto { AvaliacoesVisiveis = false };

            tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 20);
            pagina = Math.Max(1, pagina);

            var avaliacoes = await _repo.GetPublicasPorAvaliadoAsync(avaliadoId, pagina, tamanhoPagina);

            return new AvaliacoesPublicasDto
            {
                AvaliacoesVisiveis = true,
                Avaliacoes = avaliacoes
                    .Where(a => a.Nota.HasValue)
                    .Select(a => new AvaliacaoPublicaDto
                    {
                        Id = a.Id,
                        AvaliadorId = a.AvaliadorId,
                        ProjetoId = a.ContratoServico?.ProjetoId ?? 0,
                        Nota = a.Nota!.Value,
                        Comentario = a.Comentario,
                        AvaliadorNome = a.Avaliador?.Nome ?? "Usuário",
                        AvaliadorFotoUrl = ObterFotoUrl(a.Avaliador),
                        ProjetoTitulo = a.ContratoServico?.Projeto?.Titulo ?? "",
                        AvaliadorEhContratante = a.AvaliadorId == (a.ContratoServico?.ContratanteId ?? -1),
                        PublicadaEm = a.PublicadaEm ?? a.CriadoEm,
                        RespostaPublica = a.RespostaPublica,
                        RespostaPublicaCriadaEm = a.RespostaPublicaCriadaEm
                    })
                    .ToList()
            };
        }

        public async Task<List<MinhaAvaliacaoDto>> ListarMinhasAsync(int userId)
        {
            var todas = await _repo.GetMinhasAsync(userId);

            return todas.Select(a =>
            {
                bool souAvaliador = a.AvaliadorId == userId;

                // Blind review: quando o usuário é o avaliado e a avaliação ainda está
                // Pendente, oculta nota e comentário para evitar retaliação (CT-AVAL-012)
                bool ocultarDados = !souAvaliador && a.Status == StatusAvaliacao.Pendente;

                return new MinhaAvaliacaoDto
                {
                    Id = a.Id,
                    ContratoServicoId = a.ContratoServicoId,
                    ProjetoTitulo = a.ContratoServico?.Projeto?.Titulo ?? "",
                    SouAvaliador = souAvaliador,
                    OutraParteNome = souAvaliador
                        ? (a.Avaliado?.Nome ?? "Usuário")
                        : (a.Avaliador?.Nome ?? "Usuário"),
                    Nota = ocultarDados ? null : a.Nota,
                    Comentario = a.Status == StatusAvaliacao.Publicada ? a.Comentario : null,
                    Publica = a.Publica,
                    Status = a.Status,
                    StatusDescricao = ObterStatusDescricao(a, souAvaliador),
                    PublicadaEm = a.PublicadaEm,
                    CriadoEm = a.CriadoEm
                };
            }).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // EXPIRAÇÃO AUTOMÁTICA (BackgroundService — roda 1×/dia)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ExpirarPendentesAsync()
        {
            var limite = DateTime.UtcNow.AddDays(-DiasParaExpirar);
            var expirados = await _repo.GetPendentesExpiradosAsync(limite);

            if (expirados.Count == 0) return;

            // Agrupa por contrato para processar pares
            var porContrato = expirados.GroupBy(a => a.ContratoServicoId);
            var avaliadosParaRecalcular = new HashSet<int>();

            foreach (var grupo in porContrato)
            {
                foreach (var av in grupo)
                {
                    if (av.Nota != null)
                    {
                        // Avaliação preenchida mas par ainda estava pendente → publica individualmente
                        PublicarAvaliacao(av);
                        avaliadosParaRecalcular.Add(av.AvaliadoId);
                        _logger.LogInformation(
                            "Avaliação {Id} publicada por expiração (7 dias). Avaliado: {AvaliadoId}.",
                            av.Id, av.AvaliadoId);
                    }
                    else
                    {
                        // Slot vazio — expira silenciosamente (não altera reputação)
                        av.Status = StatusAvaliacao.Oculta;
                        av.PublicadaEm = DateTime.UtcNow;
                        _logger.LogInformation(
                            "Slot de avaliação {Id} expirado silenciosamente (não preenchido).", av.Id);
                    }
                }
            }

            await _repo.SaveChangesAsync();

            foreach (var avaliadoId in avaliadosParaRecalcular)
            {
                await RecalcularReputacaoAsync(avaliadoId);
                await ReindexarSePrestadorAsync(avaliadoId);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PRIVADO — helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-indexa o embedding do avaliado se ele for um Prestador com avaliações públicas habilitadas.
        /// Silencioso — erros são absorvidos pelo próprio indexador.
        /// </summary>
        private async Task ReindexarSePrestadorAsync(int userId)
        {
            var prestador = await _prestadorRepo.GetByIdAsync(userId);
            if (prestador == null || prestador.AvaliacoesPrivado) return;

            await _prestadorIndexador.IndexarAsync(userId);
        }

        private static void PublicarAvaliacao(Avaliacao av)
        {
            av.Status = StatusAvaliacao.Publicada;
            av.PublicadaEm = DateTime.UtcNow;
        }

        private async Task RecalcularReputacaoAsync(int avaliadoId)
        {
            // Carrega todas as avaliações públicas do avaliado
            var todasPublicas = await _repo.GetPublicasPorAvaliadoAsync(avaliadoId, 1, int.MaxValue);

            // Verifica privacidade para o cálculo
            bool publica = await ResolverPrivacidadeAsync(avaliadoId);

            var notas = todasPublicas
                .Where(a => a.Nota.HasValue)
                .Select(a => a.Nota!.Value)
                .ToList();

            var resumo = new ReputacaoResumo
            {
                UsuarioId = avaliadoId,
                TotalAvaliacoes = notas.Count,
                MediaGeral = notas.Count > 0 ? Math.Round(notas.Average(), 2) : 0,
                Distribuicao1 = notas.Count(n => n == 1),
                Distribuicao2 = notas.Count(n => n == 2),
                Distribuicao3 = notas.Count(n => n == 3),
                Distribuicao4 = notas.Count(n => n == 4),
                Distribuicao5 = notas.Count(n => n == 5),
                UltimaAtualizacao = DateTime.UtcNow
            };

            await _repo.UpsertReputacaoAsync(resumo);
            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Reputação recalculada para usuário {Id}: média {Media} ({Total} avaliações públicas).",
                avaliadoId, resumo.MediaGeral, resumo.TotalAvaliacoes);
        }

        // RN-AV-001/012: altera a configuração AvaliacoesPrivado com efeito imediato
        public async Task AlterarPrivacidadeAvaliacoesAsync(int userId, bool privado)
        {
            var prestador = await _prestadorRepo.GetByIdAsync(userId);
            if (prestador != null)
            {
                prestador.AvaliacoesPrivado = privado;
                await _prestadorRepo.SaveAsync();
                return;
            }

            var contratante = await _contratanteRepo.GetByIdAsync(userId);
            if (contratante != null)
            {
                contratante.AvaliacoesPrivado = privado;
                await _contratanteRepo.SaveAsync();
                return;
            }

            throw new NegocioException("Usuário não encontrado.");
        }

        private async Task<bool> ResolverPrivacidadeAsync(int userId)
        {
            // Tenta Prestador primeiro
            var prestador = await _prestadorRepo.GetByIdAsync(userId);
            if (prestador != null) return !prestador.AvaliacoesPrivado;

            // Tenta Contratante
            var contratante = await _contratanteRepo.GetByIdAsync(userId);
            if (contratante != null) return !contratante.AvaliacoesPrivado;

            return false;
        }

        private static AvaliacaoPendenteDto MapearParaPendenteDto(Avaliacao a)
        {
            return new AvaliacaoPendenteDto
            {
                Id = a.Id,
                ContratoServicoId = a.ContratoServicoId,
                ProjetoTitulo = a.ContratoServico?.Projeto?.Titulo ?? "",
                AvaliadoNome = a.Avaliado?.Nome ?? "Usuário",
                AvaliadoFotoUrl = ObterFotoUrl(a.Avaliado),
                JaEnviou = a.Nota != null,
                CriadoEm = a.CriadoEm
            };
        }

        private static string ObterFotoUrl(Usuario? usuario)
        {
            if (usuario is Domain.Models.Prestador.Prestador p) return p.FotoUrl ?? "";
            if (usuario is Domain.Models.Contratante c) return c.LogoUrl ?? "";
            return "";
        }

        /// <summary>
        /// Texto amigável para o status da avaliação no contexto do usuário logado (melhoria #1).
        /// </summary>
        private static string ObterStatusDescricao(Avaliacao a, bool souAvaliador)
        {
            return a.Status switch
            {
                StatusAvaliacao.Publicada => "Publicada",
                StatusAvaliacao.Oculta => "Encerrada (não avaliada)",
                StatusAvaliacao.Pendente when souAvaliador && a.Nota != null
                    => "Aguardando outra parte avaliar",
                StatusAvaliacao.Pendente when souAvaliador
                    => "Pendente — avalie agora",
                StatusAvaliacao.Pendente
                    => "Será publicada em até 7 dias",
                _ => a.Status.ToString()
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RESPOSTA PÚBLICA À AVALIAÇÃO (melhoria #2)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ResponderAvaliacaoAsync(Guid avaliacaoId, int userId, ResponderAvaliacaoDto dto)
        {
            var avaliacao = await _repo.GetByIdAsync(avaliacaoId)
                ?? throw new NegocioException("Avaliação não encontrada.");

            if (avaliacao.AvaliadoId != userId)
                throw new NegocioException("Apenas o avaliado pode responder a esta avaliação.");

            if (avaliacao.Status != StatusAvaliacao.Publicada)
                throw new NegocioException("Só é possível responder a avaliações publicadas.");

            if (avaliacao.RespostaPublica != null)
                throw new NegocioException("Você já respondeu a esta avaliação.");

            if (string.IsNullOrWhiteSpace(dto.Resposta))
                throw new NegocioException("A resposta não pode estar vazia.");

            if (dto.Resposta.Length > 500)
                throw new NegocioException("A resposta não pode ultrapassar 500 caracteres.");

            avaliacao.RespostaPublica = dto.Resposta.Trim();
            avaliacao.RespostaPublicaCriadaEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Resposta pública registrada na avaliação {Id} pelo avaliado {UserId}.",
                avaliacaoId, userId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LEMBRETE D+3 (melhoria #4)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task EnviarLembretesD3Async()
        {
            var limiteD3 = DateTime.UtcNow.AddDays(-3);
            var limiteD4 = DateTime.UtcNow.AddDays(-4); // janela de 1 dia para não reenviar

            var pendentes = await _repo.GetPendentesParaLembreteAsync(limiteD4, limiteD3);

            if (pendentes.Count == 0) return;

            foreach (var av in pendentes)
            {
                try
                {
                    var avaliador = av.Avaliador;
                    if (avaliador?.Email == null) continue;

                    var projetoTitulo = av.ContratoServico?.Projeto?.Titulo ?? "Projeto";

                    await _emailService.EnviarLembreteAvaliacaoPendenteAsync(
                        avaliador.Email,
                        avaliador.Nome ?? "Usuário",
                        projetoTitulo);

                    _logger.LogInformation(
                        "Lembrete D+3 enviado para {Email} — avaliação {Id}.",
                        avaliador.Email, av.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Falha ao enviar lembrete D+3 para avaliação {Id}.", av.Id);
                }
            }
        }
    }
}
