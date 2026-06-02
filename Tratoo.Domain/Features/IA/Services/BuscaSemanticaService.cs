using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.IA
{
    /// <summary>
    /// Busca semântica em duas camadas:
    ///   1. PostgreSQL + pgvector — top-100 por distância de cosseno (<=> HNSW).
    ///   2. C# — filtra por regras de negócio e aplica score composto.
    ///
    /// Score (soma = 1.00):
    ///   Semântica 35% | Habilidades exatas 15% | Reputação 15% | Perfil 10%
    ///   Contratos 10% | Verificação 10% | Disponibilidade 5%
    /// </summary>
    public class BuscaSemanticaService
    {
        private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly TratooContext _db;
        private readonly VectorContext _vectorDb;
        private readonly IEmbeddingService _ai;
        private readonly ILogger<BuscaSemanticaService> _logger;

        public BuscaSemanticaService(
            TratooContext db,
            VectorContext vectorDb,
            IEmbeddingService ai,
            ILogger<BuscaSemanticaService> logger)
        {
            _db = db;
            _vectorDb = vectorDb;
            _ai = ai;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // BUSCA DE PRESTADORES (para contratantes)
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<List<PrestadorBuscaResultDTO>> BuscarPrestadoresAsync(
            FiltrosBuscaPrestadoresDTO filtros)
        {
            filtros.PageSize = Math.Min(filtros.PageSize, 20);

            var termosBusca = ExtrairTermosBusca(filtros.Q, filtros.Categoria);

            // ── Camada 1: pgvector — top-100 por similaridade semântica ───────────
            float[]? queryVec = null;
            bool aiDisponivel = true;
            if (!string.IsNullOrWhiteSpace(filtros.Q))
            {
                try { queryVec = await _ai.GerarEmbeddingAsync(filtros.Q); }
                catch (Exception ex)
                {
                    aiDisponivel = false;
                    _logger.LogWarning(ex, "AI Service indisponível — fallback para busca por palavras-chave.");
                }
            }

            Dictionary<int, float> similaridades;

            if (queryVec != null)
            {
                var qv = new Vector(queryVec);
                var topVetorial = await _vectorDb.PrestadorEmbeddings
                    .OrderBy(e => e.Embedding.CosineDistance(qv))
                    .Take(100)
                    .Select(e => new { e.PrestadorId, Distance = e.Embedding.CosineDistance(qv) })
                    .ToListAsync();

                if (!topVetorial.Any()) return [];
                similaridades = topVetorial.ToDictionary(t => t.PrestadorId, t => 1f - (float)t.Distance);
            }
            else if (!aiDisponivel && !string.IsNullOrWhiteSpace(filtros.Q))
            {
                // Fallback textual
                var todos = await _vectorDb.PrestadorEmbeddings.AsNoTracking().ToListAsync();
                similaridades = todos
                    .Where(e => termosBusca.Any(t => e.TextoNormalizado.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    .ToDictionary(e => e.PrestadorId, _ => 0.5f);
                if (!similaridades.Any()) return [];
            }
            else
            {
                var todosIds = await _vectorDb.PrestadorEmbeddings.Select(e => e.PrestadorId).ToListAsync();
                similaridades = todosIds.ToDictionary(id => id, _ => 0.5f);
            }

            var candidatoIds = similaridades.Keys.ToList();

            // ── Camada 2: SQL Server — dados dos candidatos ───────────────────────
            var query = _db.Prestadores
                .Where(p => p.Status == StatusUsuario.Active && candidatoIds.Contains(p.Id));

            if (filtros.ValorHoraMax.HasValue)
                query = query.Where(p => p.ValorHora == null || p.ValorHora <= filtros.ValorHoraMax);

            if (!string.IsNullOrWhiteSpace(filtros.Categoria))
                query = query.Where(p => p.Competencias.Any(c => c.Nome.Contains(filtros.Categoria)));

            var candidatosBase = await query
                .AsNoTracking()
                .Select(p => new
                {
                    p.Id,
                    p.Nome,
                    p.TituloProfissional,
                    p.FotoUrl,
                    p.ValorHora,
                    p.PorcentagemCompleto,
                    p.AvaliacoesPrivado,
                    p.Disponivel,
                    p.DisponivelAPartirDe,
                    Bio = p.Descricao,
                    Competencias = p.Competencias.Select(c => c.Nome).ToList()
                })
                .ToListAsync();

            if (!candidatosBase.Any()) return [];

            var filtradosIds = candidatosBase.Select(c => c.Id).ToHashSet();
            var privados = candidatosBase.Where(c => c.AvaliacoesPrivado).Select(c => c.Id).ToHashSet();

            var reputacoes = await _db.ReputacaoResumos
                .AsNoTracking()
                .Where(r => filtradosIds.Contains(r.UsuarioId))
                .ToDictionaryAsync(r => r.UsuarioId, r => new { r.MediaGeral, r.TotalAvaliacoes });

            var identidades = await _db.UserIdentities
                .AsNoTracking()
                .Where(i => filtradosIds.Contains(i.UserId))
                .ToDictionaryAsync(i => i.UserId, i => (int)i.NivelVerificacao);

            var contratosEncerrados = await _db.ContratosServico
                .AsNoTracking()
                .Where(c => filtradosIds.Contains(c.PrestadorId) && c.Status == ContratoServicoStatus.Encerrado)
                .GroupBy(c => c.PrestadorId)
                .Select(g => new { PrestadorId = g.Key, Total = g.Count() })
                .ToDictionaryAsync(g => g.PrestadorId, g => g.Total);

            // ── Filtros pós-carga ─────────────────────────────────────────────────
            if (filtros.ApenasVerificados == true)
                filtradosIds = filtradosIds.Where(id => identidades.GetValueOrDefault(id) >= 2).ToHashSet();

            // RN-AV-007: prestadores com avaliações privadas ignoram filtro avaliacaoMin
            if (filtros.AvaliacaoMin.HasValue)
                filtradosIds = filtradosIds
                    .Where(id => !privados.Contains(id) &&
                                 (reputacoes.TryGetValue(id, out var r) ? r.MediaGeral : 0) >= filtros.AvaliacaoMin.Value)
                    .ToHashSet();

            // ── Score composto ────────────────────────────────────────────────────
            var resultados = new List<PrestadorBuscaResultDTO>();
            foreach (var c in candidatosBase.Where(c => filtradosIds.Contains(c.Id)))
            {
                float similaridade = similaridades.GetValueOrDefault(c.Id, 0.5f);

                var (stackScore, matched) = CalcularSimilaridadeStack(c.Competencias, termosBusca);

                // RN-AV-006: avaliações privadas não influenciam ranking
                var rep = reputacoes.GetValueOrDefault(c.Id);
                double mediaAval = privados.Contains(c.Id) ? 0 : (rep?.MediaGeral ?? 0);
                int totalAval    = privados.Contains(c.Id) ? 0 : (rep?.TotalAvaliacoes ?? 0);
                int nivelVerif   = identidades.GetValueOrDefault(c.Id);
                int contratos    = contratosEncerrados.GetValueOrDefault(c.Id);

                // Semântica 42% | Stack exata 31% | Reputação 05% | Perfil 12% | Contratos 05% | Verificação 05%
                float score = (float)(
                similaridade * 0.50 +
                stackScore * 0.25 +
                (mediaAval / 5.0) * 0.08 +
                (c.PorcentagemCompleto / 100.0) * 0.10 +
                (Math.Min(contratos, 20) / 20.0) * 0.04 +
                (nivelVerif >= 2 ? 1.0 : 0.3) * 0.03
            );

                resultados.Add(new PrestadorBuscaResultDTO
                {
                    Id                   = c.Id,
                    Nome                 = c.Nome,
                    TituloProfissional   = c.TituloProfissional,
                    FotoUrl              = c.FotoUrl,
                    ValorHora            = c.ValorHora,
                    MediaAvaliacoes      = mediaAval,
                    TotalAvaliacoes      = totalAval,
                    PorcentagemCompleto  = c.PorcentagemCompleto,
                    NivelVerificacao     = nivelVerif,
                    ContratosEncerrados  = contratos,
                    Competencias         = c.Competencias.Take(5).ToList(),
                    Bio                  = TruncarBio(c.Bio),
                    Similaridade         = similaridade,
                    ScoreComposto        = score,
                    CompetenciasMatchadas = matched
                });
            }

            var offset = (filtros.Page - 1) * filtros.PageSize;
            return resultados
                .OrderByDescending(r => r.ScoreComposto)
                .Skip(offset)
                .Take(filtros.PageSize)
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PRESTADORES SIMILARES a um perfil específico
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<List<PrestadorBuscaResultDTO>> BuscarSimilaresAsync(
            int prestadorReferencia, int quantidade = 5)
        {
            var embRef = await _vectorDb.PrestadorEmbeddings.FindAsync(prestadorReferencia);
            if (embRef == null) return [];

            var resultado = await BuscarPrestadoresAsync(new FiltrosBuscaPrestadoresDTO
            {
                Q        = embRef.TextoNormalizado,
                Page     = 1,
                PageSize = quantidade + 1
            });

            return resultado.Where(r => r.Id != prestadorReferencia).Take(quantidade).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PROJETOS RECOMENDADOS PARA O PRESTADOR LOGADO
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<List<ProjetoBuscaResultDTO>> RecomendarProjetosAsync(
            int prestadorId, string? queryAdicional = null)
        {
            var embPrestador = await _vectorDb.PrestadorEmbeddings.FindAsync(prestadorId);
            if (embPrestador == null) return [];

            float[] vecBusca = embPrestador.Embedding.ToArray();

            if (!string.IsNullOrWhiteSpace(queryAdicional))
            {
                try
                {
                    var qv = await _ai.GerarEmbeddingAsync(queryAdicional);
                    vecBusca = vecBusca.Zip(qv, (a, b) => (a + b) / 2f).ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao gerar embedding da query adicional — usando só o perfil.");
                }
            }

            var propostasAtivas = await _db.PropostasProjeto
                .Where(pp => pp.PrestadorId == prestadorId &&
                             (pp.Status == StatusPropostaProjeto.Convertida ||
                              pp.Status == StatusPropostaProjeto.Aceita))
                .Select(pp => pp.ProjetoId)
                .ToListAsync();

            var projetosAbertosIds = await _db.Projetos
                .Where(p => p.Status == StatusProjeto.Aberto
                         && p.Visibilidade == VisibilidadeProjeto.Publico
                         && !propostasAtivas.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();

            if (!projetosAbertosIds.Any()) return [];

            var qvec = new Vector(vecBusca);
            var topProjetos = await _vectorDb.ProjetoEmbeddings
                .Where(e => projetosAbertosIds.Contains(e.ProjetoId))
                .OrderBy(e => e.Embedding.CosineDistance(qvec))
                .Take(50)
                .Select(e => new { e.ProjetoId, Distance = e.Embedding.CosineDistance(qvec) })
                .ToListAsync();

            if (!topProjetos.Any()) return [];

            var simProjetos = topProjetos.ToDictionary(t => t.ProjetoId, t => 1f - (float)t.Distance);
            var idsComEmb = topProjetos.Select(t => t.ProjetoId).ToList();

            var projetos = await _db.Projetos
                .AsNoTracking()
                .Where(p => idsComEmb.Contains(p.Id))
                .Select(p => new
                {
                    p.Id, p.Titulo, p.Descricao,
                    Categoria = p.Categoria.ToString(),
                    p.OrcamentoMin, p.OrcamentoMax, p.PrazoEntrega, p.TotalPropostas, p.Habilidades
                })
                .ToDictionaryAsync(p => p.Id);

            return topProjetos
                .Where(t => projetos.ContainsKey(t.ProjetoId))
                .Select(t =>
                {
                    var proj = projetos[t.ProjetoId];
                    return new ProjetoBuscaResultDTO
                    {
                        Id             = proj.Id,
                        Titulo         = proj.Titulo,
                        Descricao      = proj.Descricao.Length > 200 ? proj.Descricao[..200] + "..." : proj.Descricao,
                        Categoria      = proj.Categoria,
                        OrcamentoMin   = proj.OrcamentoMin,
                        OrcamentoMax   = proj.OrcamentoMax,
                        PrazoEntrega   = proj.PrazoEntrega,
                        TotalPropostas = proj.TotalPropostas,
                        Habilidades    = ParseHabilidades(proj.Habilidades),
                        Similaridade   = simProjetos[t.ProjetoId]
                    };
                })
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // BUSCA PÚBLICA DE PROJETOS
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<List<ProjetoBuscaResultDTO>> BuscarProjetosAsync(
            string? query, int page = 1, int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 20);

            var projetoIds = await _db.Projetos
                .Where(p => p.Status == StatusProjeto.Aberto && p.Visibilidade == VisibilidadeProjeto.Publico)
                .Select(p => p.Id)
                .ToListAsync();

            if (!projetoIds.Any()) return [];

            Dictionary<int, float> similaridades;

            if (!string.IsNullOrWhiteSpace(query))
            {
                float[]? qv = null;
                try { qv = await _ai.GerarEmbeddingAsync(query); }
                catch (Exception ex) { _logger.LogWarning(ex, "AI indisponível — fallback sem ranking semântico."); }

                if (qv != null)
                {
                    var qvec = new Vector(qv);
                    var top = await _vectorDb.ProjetoEmbeddings
                        .Where(e => projetoIds.Contains(e.ProjetoId))
                        .OrderBy(e => e.Embedding.CosineDistance(qvec))
                        .Take(100)
                        .Select(e => new { e.ProjetoId, Distance = e.Embedding.CosineDistance(qvec) })
                        .ToListAsync();
                    similaridades = top.ToDictionary(t => t.ProjetoId, t => 1f - (float)t.Distance);
                }
                else
                {
                    var todos = await _vectorDb.ProjetoEmbeddings
                        .Where(e => projetoIds.Contains(e.ProjetoId)).Select(e => e.ProjetoId).ToListAsync();
                    similaridades = todos.ToDictionary(id => id, _ => 0.5f);
                }
            }
            else
            {
                var todos = await _vectorDb.ProjetoEmbeddings
                    .Where(e => projetoIds.Contains(e.ProjetoId)).Select(e => e.ProjetoId).ToListAsync();
                similaridades = todos.ToDictionary(id => id, _ => 0.5f);
            }

            if (!similaridades.Any()) return [];

            var idsComEmb = similaridades.Keys.ToList();
            var projetos = await _db.Projetos
                .AsNoTracking()
                .Where(p => idsComEmb.Contains(p.Id))
                .Select(p => new
                {
                    p.Id, p.Titulo, p.Descricao,
                    Categoria = p.Categoria.ToString(),
                    p.OrcamentoMin, p.OrcamentoMax, p.PrazoEntrega, p.TotalPropostas, p.Habilidades
                })
                .ToDictionaryAsync(p => p.Id);

            var offset = (page - 1) * pageSize;
            return similaridades
                .Where(kv => projetos.ContainsKey(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .Skip(offset)
                .Take(pageSize)
                .Select(kv =>
                {
                    var proj = projetos[kv.Key];
                    return new ProjetoBuscaResultDTO
                    {
                        Id             = proj.Id,
                        Titulo         = proj.Titulo,
                        Descricao      = proj.Descricao.Length > 200 ? proj.Descricao[..200] + "..." : proj.Descricao,
                        Categoria      = proj.Categoria,
                        OrcamentoMin   = proj.OrcamentoMin,
                        OrcamentoMax   = proj.OrcamentoMax,
                        PrazoEntrega   = proj.PrazoEntrega,
                        TotalPropostas = proj.TotalPropostas,
                        Habilidades    = ParseHabilidades(proj.Habilidades),
                        Similaridade   = kv.Value
                    };
                })
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PRESTADORES RECOMENDADOS PARA UM PROJETO (para contratantes)
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<List<PrestadorBuscaResultDTO>?> RecomendarPrestadoresParaProjetoAsync(
            int projetoId, int contratanteId, int quantidade = 10)
        {
            quantidade = Math.Min(quantidade, 20);

            var projeto = await _db.Projetos
                .Where(p => p.Id == projetoId && p.ContratanteId == contratanteId)
                .Select(p => new { p.Id, p.Titulo, p.Descricao, p.Habilidades })
                .FirstOrDefaultAsync();

            if (projeto == null) return null;

            // Habilidades do projeto para skill-matching
            var projetoHabs = ParseHabilidades(projeto.Habilidades);
            var termosProjeto = projetoHabs.Select(h => h.ToLowerInvariant()).ToHashSet();

            // Embedding do projeto
            Vector projetoVec;
            var embProjeto = await _vectorDb.ProjetoEmbeddings.FindAsync(projetoId);
            if (embProjeto != null)
            {
                projetoVec = embProjeto.Embedding;
            }
            else
            {
                var arr = await GerarVetorProjetoAsync(projeto.Titulo, projeto.Descricao);
                if (arr == null) return [];
                projetoVec = new Vector(arr);
            }

            // pgvector: top-100 prestadores mais parecidos com o projeto
            var topPrestadores = await _vectorDb.PrestadorEmbeddings
                .OrderBy(e => e.Embedding.CosineDistance(projetoVec))
                .Take(100)
                .Select(e => new { e.PrestadorId, Distance = e.Embedding.CosineDistance(projetoVec) })
                .ToListAsync();

            if (!topPrestadores.Any()) return [];

            var similaridades = topPrestadores.ToDictionary(t => t.PrestadorId, t => 1f - (float)t.Distance);
            var candidatoIds = topPrestadores.Select(t => t.PrestadorId).ToList();

            var candidatosBase = await _db.Prestadores
                .AsNoTracking()
                .Where(p => p.Status == StatusUsuario.Active && candidatoIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id, p.Nome, p.TituloProfissional, p.FotoUrl, p.ValorHora,
                    p.PorcentagemCompleto, p.AvaliacoesPrivado,
                    p.Disponivel, p.DisponivelAPartirDe,
                    Bio = p.Descricao,
                    Competencias = p.Competencias.Select(c => c.Nome).ToList()
                })
                .ToListAsync();

            if (!candidatosBase.Any()) return [];

            var filtradosIds = candidatosBase.Select(c => c.Id).ToHashSet();
            var privados = candidatosBase.Where(c => c.AvaliacoesPrivado).Select(c => c.Id).ToHashSet();

            var reputacoes = await _db.ReputacaoResumos
                .AsNoTracking()
                .Where(r => filtradosIds.Contains(r.UsuarioId))
                .ToDictionaryAsync(r => r.UsuarioId, r => new { r.MediaGeral, r.TotalAvaliacoes });

            var identidades = await _db.UserIdentities
                .AsNoTracking()
                .Where(i => filtradosIds.Contains(i.UserId))
                .ToDictionaryAsync(i => i.UserId, i => (int)i.NivelVerificacao);

            var contratosEncerrados = await _db.ContratosServico
                .AsNoTracking()
                .Where(c => filtradosIds.Contains(c.PrestadorId) && c.Status == ContratoServicoStatus.Encerrado)
                .GroupBy(c => c.PrestadorId)
                .Select(g => new { PrestadorId = g.Key, Total = g.Count() })
                .ToDictionaryAsync(g => g.PrestadorId, g => g.Total);

            var resultados = new List<PrestadorBuscaResultDTO>();
            foreach (var c in candidatosBase)
            {
                float similaridade = similaridades.GetValueOrDefault(c.Id, 0f);

                // termosProjeto vem das habilidades reais do projeto — matching preciso
                var (stackScore, matched) = CalcularSimilaridadeStack(c.Competencias, termosProjeto);

                var rep = reputacoes.GetValueOrDefault(c.Id);
                double mediaAval = privados.Contains(c.Id) ? 0 : (rep?.MediaGeral ?? 0);
                int totalAval    = privados.Contains(c.Id) ? 0 : (rep?.TotalAvaliacoes ?? 0);
                int nivelVerif   = identidades.GetValueOrDefault(c.Id);
                int contratos    = contratosEncerrados.GetValueOrDefault(c.Id);

                float score = (float)(
                    similaridade * 0.50 +
                    stackScore * 0.25 +
                    (mediaAval / 5.0) * 0.08 +
                    (c.PorcentagemCompleto / 100.0) * 0.10 +
                    (Math.Min(contratos, 20) / 20.0) * 0.04 +
                    (nivelVerif >= 2 ? 1.0 : 0.3) * 0.03
                );

                resultados.Add(new PrestadorBuscaResultDTO
                {
                    Id                   = c.Id,
                    Nome                 = c.Nome,
                    TituloProfissional   = c.TituloProfissional,
                    FotoUrl              = c.FotoUrl,
                    ValorHora            = c.ValorHora,
                    MediaAvaliacoes      = mediaAval,
                    TotalAvaliacoes      = totalAval,
                    PorcentagemCompleto  = c.PorcentagemCompleto,
                    NivelVerificacao     = nivelVerif,
                    ContratosEncerrados  = contratos,
                    Competencias         = c.Competencias.Take(5).ToList(),
                    Bio                  = TruncarBio(c.Bio),
                    Similaridade         = similaridade,
                    ScoreComposto        = score,
                    CompetenciasMatchadas = matched
                });
            }

            return resultados.OrderByDescending(r => r.ScoreComposto).Take(quantidade).ToList();
        }

        private async Task<float[]?> GerarVetorProjetoAsync(string titulo, string descricao)
        {
            try { return await _ai.GerarEmbeddingAsync($"{titulo} {descricao}"); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Service indisponível — não foi possível gerar embedding do projeto.");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // STATUS DE INDEXAÇÃO (para admin)
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<StatusIndexacaoDTO> ObterStatusAsync()
        {
            var totalPrestadores    = await _db.Prestadores.CountAsync();
            var totalPrestadoresIdx = await _vectorDb.PrestadorEmbeddings.CountAsync();

            var totalProjetos    = await _db.Projetos.CountAsync(p => p.Status == StatusProjeto.Aberto && p.Publicado);
            var totalProjetosIdx = await _vectorDb.ProjetoEmbeddings.CountAsync();

            var ultimaPrestador = await _vectorDb.PrestadorEmbeddings.MaxAsync(e => (DateTime?)e.IndexadoEm);
            var ultimaProjeto   = await _vectorDb.ProjetoEmbeddings.MaxAsync(e => (DateTime?)e.IndexadoEm);

            return new StatusIndexacaoDTO
            {
                TotalPrestadoresIndexados = totalPrestadoresIdx,
                TotalProjetosIndexados    = totalProjetosIdx,
                PrestadoresSemEmbedding   = totalPrestadores - totalPrestadoresIdx,
                ProjetosSemEmbedding      = Math.Max(0, totalProjetos - totalProjetosIdx),
                UltimaIndexacaoPrestador  = ultimaPrestador,
                UltimaIndexacaoProjeto    = ultimaProjeto
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Extrai termos relevantes da query livre e da categoria para uso no skill-matching.
        /// Remove stop words e termos genéricos de ocupação (desenvolvedor, analista…).
        /// </summary>
        private static HashSet<string> ExtrairTermosBusca(string? q, string? categoria = null)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "de", "do", "da", "dos", "das", "para", "com", "em", "um", "uma",
                "por", "que", "ou", "e", "a", "o", "as", "os",
                "desenvolvedor", "desenvolvedora", "programador", "analista",
                "engenheiro", "especialista", "profissional", "senior", "junior", "pleno"
            };

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(q))
            {
                foreach (var t in q.Split(new[] { ' ', ',', ';', '/', '.' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (t.Length >= 2 && !stopWords.Contains(t))
                        result.Add(t.ToLowerInvariant());
                }
            }

            if (!string.IsNullOrWhiteSpace(categoria))
                result.Add(categoria.ToLowerInvariant());

            return result;
        }

        // Sinônimos técnicos — expande termos de busca para cobrir variantes comuns de nomenclatura
        private static readonly Dictionary<string, string[]> _techSinonimos =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"]          = ["csharp", ".net", "dotnet", "asp.net"],
            ["csharp"]      = ["c#", ".net", "dotnet", "asp.net"],
            [".net"]        = ["c#", "csharp", "dotnet", "asp.net core", "asp.net"],
            ["dotnet"]      = ["c#", "csharp", ".net", "asp.net"],
            ["asp.net"]     = ["c#", ".net", "dotnet", "asp.net core"],
            ["javascript"]  = ["js", "node", "nodejs", "typescript"],
            ["js"]          = ["javascript", "typescript"],
            ["typescript"]  = ["ts", "javascript", "js"],
            ["ts"]          = ["typescript", "javascript"],
            ["node"]        = ["nodejs", "javascript", "js"],
            ["nodejs"]      = ["node", "javascript", "js"],
            ["react"]       = ["reactjs", "react.js"],
            ["reactjs"]     = ["react"],
            ["vue"]         = ["vuejs", "vue.js"],
            ["angular"]     = ["angularjs"],
            ["postgresql"]  = ["postgres", "pg"],
            ["postgres"]    = ["postgresql"],
            ["mongodb"]     = ["mongo"],
            ["mongo"]       = ["mongodb"],
            ["kubernetes"]  = ["k8s"],
            ["python"]      = ["py", "django", "fastapi", "flask"],
            ["java"]        = ["spring", "springboot"],
            ["docker"]      = ["container", "containers"],
            ["aws"]         = ["amazon", "cloud"],
            ["azure"]       = ["microsoft azure", "cloud"],
        };

        /// <summary>
        /// Calcula a proporção de skills requeridas (pelo projeto/query) que o prestador possui.
        /// Score = habilidades_encontradas / habilidades_requeridas (0..1).
        /// Usa sinônimos técnicos para cobrir variantes (.NET, C#, csharp → mesmo grupo).
        /// Quando não há termos de busca, retorna score neutro 0.5.
        /// </summary>
        private static (float score, List<string> matched) CalcularSimilaridadeStack(
            IEnumerable<string> competencias, IReadOnlyCollection<string> termosRequeridos)
        {
            if (termosRequeridos.Count == 0) return (0.5f, []);

            var competenciasLower = competencias
                .Select(c => c.ToLowerInvariant())
                .ToList();

            var matched = new List<string>();

            foreach (var termo in termosRequeridos)
            {
                var termoL = termo.ToLowerInvariant();

                // Sinônimos que expandem o termo buscado
                var variantes = _techSinonimos.TryGetValue(termoL, out var s)
                    ? s.Append(termoL).ToArray()
                    : [termoL];

                var competenciaMatch = competenciasLower
                    .FirstOrDefault(c => variantes.Any(v =>
                        c == v ||
                        c.Contains(v) ||
                        (v.Length >= 3 && v.Contains(c))));

                if (competenciaMatch != null)
                {
                    // Devolve o nome original (não o lowercased)
                    var original = competencias
                        .FirstOrDefault(c => c.ToLowerInvariant() == competenciaMatch)
                            ?? competenciaMatch;
                    matched.Add(original);
                }
            }

            // Score: proporção de skills do projeto que o prestador tem
            float score = (float)matched.Count / termosRequeridos.Count;
            return (score, matched.Distinct().ToList());
        }

        private static string? TruncarBio(string? bio)
        {
            if (string.IsNullOrWhiteSpace(bio)) return null;
            return bio.Length > 150 ? bio[..150] + "..." : bio;
        }

        private static List<string> ParseHabilidades(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];
            try { return JsonSerializer.Deserialize<List<string>>(json, _jsonOpts) ?? []; }
            catch { return []; }
        }
    }
}
