using System.Text.Json;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Projetos
{
    public class ProjetoService
    {
        private readonly IProjetoRepository _repo;
        private readonly IContratanteRepository _contratanteRepo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly ProjetoIndexadorService _indexador;

        public ProjetoService(
            IProjetoRepository repo,
            IContratanteRepository contratanteRepo,
            IIdentidadeRepository identidadeRepo,
            ProjetoIndexadorService indexador)
        {
            _repo = repo;
            _contratanteRepo = contratanteRepo;
            _identidadeRepo = identidadeRepo;
            _indexador = indexador;
        }

        public async Task<ProjetoResumoDTO> CriarAsync(CriarProjetoDTO dto)
        {
            var contratante = await _contratanteRepo.GetByIdAsync(dto.ContratanteId)
                ?? throw new NegocioException("Contratante não encontrado.");

            ValidarHabilidades(dto.Habilidades);

            if (string.IsNullOrWhiteSpace(dto.Titulo) || dto.Titulo.Trim().Length < 10)
                throw new NegocioException("Informe um título de pelo menos 10 caracteres.");

            if (string.IsNullOrWhiteSpace(dto.Descricao) || dto.Descricao.Trim().Length < 100)
                throw new NegocioException("Descreva o projeto com pelo menos 100 caracteres.");

            if (dto.OrcamentoMin <= 0 || dto.OrcamentoMax < dto.OrcamentoMin)
                throw new NegocioException("Valor de orçamento inválido.");

            if (dto.PrazoEntrega.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("O prazo de entrega deve ser uma data futura.");

            if (dto.NumFreelancersDesejados < 1)
                throw new NegocioException("Informe um número válido de freelancers.");

            var habilidadesJson = dto.Habilidades != null && dto.Habilidades.Count > 0
                ? JsonSerializer.Serialize(dto.Habilidades)
                : null;

            var projeto = new Projeto
            {
                ContratanteId = dto.ContratanteId,
                Titulo = dto.Titulo,
                Descricao = dto.Descricao,
                Categoria = dto.Categoria,
                OrcamentoMin = dto.OrcamentoMin,
                OrcamentoMax = dto.OrcamentoMax,
                PrazoEntrega = dto.PrazoEntrega,
                Habilidades = habilidadesJson,
                NivelFreelancer = dto.NivelFreelancer,
                Visibilidade = dto.Visibilidade,
                Idioma = dto.Idioma,
                NumFreelancersDesejados = dto.NumFreelancersDesejados,
                Status = StatusProjeto.Rascunho,
                CriadoEm = DateTime.UtcNow,
                AtualizadoEm = DateTime.UtcNow
            };

            await _repo.AddAsync(projeto);
            await _repo.SaveChangesAsync();

            if (dto.PublicarImediatamente)
                await PublicarInternoAsync(projeto, dto.ContratanteId);

            projeto.Contratante = contratante;
            return MapResumo(projeto);
        }

        public async Task PublicarAsync(int projetoId, int contratanteId)
        {
            var projeto = await _repo.GetByIdAsync(projetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            await PublicarInternoAsync(projeto, contratanteId);
        }

        private async Task PublicarInternoAsync(Projeto projeto, int contratanteId)
        {
            if (projeto.ContratanteId != contratanteId)
                throw new NegocioException("Você não tem permissão para publicar este projeto.");

            if (projeto.Status != StatusProjeto.Rascunho)
                throw new NegocioException("Somente rascunhos podem ser publicados.");

            // Verifica nível de verificação (NivelVerificacao >= Identidade = 2)
            var identity = await _identidadeRepo.ObterPorUserIdAsync(contratanteId);
            if (identity == null || (int)identity.NivelVerificacao < (int)NivelVerificacao.Identidade)
                throw new NegocioException("Valide seu CPF/CNPJ antes de publicar projetos.");

            ValidarCamposObrigatoriosPublicacao(projeto);

            projeto.Status = StatusProjeto.Aberto;
            projeto.Publicado = true;
            projeto.PublicadoEm = DateTime.UtcNow;
            projeto.AtualizadoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            // Indexação semântica após publicação — silenciosa, não bloqueia o retorno
            await _indexador.IndexarAsync(projeto.Id);
        }

        public async Task<PaginadoDTO<ProjetoResumoDTO>> BuscarAsync(FiltrosProjetoDTO filtros)
        {
            var (projetos, total) = await _repo.BuscarAsync(filtros);
            var pagina = Math.Max(1, filtros.Page);
            var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / 20.0);

            return new PaginadoDTO<ProjetoResumoDTO>
            {
                Itens = projetos.Select(MapResumo).ToList(),
                Total = total,
                Pagina = pagina,
                TotalPaginas = totalPaginas
            };
        }

        public async Task<ProjetoDetalheDTO?> ObterDetalheAsync(int id)
        {
            var p = await _repo.GetByIdAsync(id);
            return p == null ? null : MapDetalhe(p);
        }

        public async Task<List<ProjetoResumoDTO>> GetDoContratanteAsync(int contratanteId)
        {
            var projetos = await _repo.GetDoContratanteAsync(contratanteId);
            return projetos.Select(MapResumo).ToList();
        }

        public async Task CancelarAsync(int projetoId, int contratanteId, string? motivo)
        {
            var projeto = await _repo.GetByIdAsync(projetoId)
                ?? throw new NegocioException("Projeto não encontrado.");

            if (projeto.ContratanteId != contratanteId)
                throw new NegocioException("Você não tem permissão para cancelar este projeto.");

            if (projeto.Status != StatusProjeto.Rascunho && projeto.Status != StatusProjeto.Aberto)
                throw new NegocioException("Projeto não pode ser cancelado neste status.");

            projeto.Status = StatusProjeto.Cancelado;
            projeto.CanceladoEm = DateTime.UtcNow;
            projeto.MotivoCancelamento = motivo;
            projeto.AtualizadoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();
        }

        // ────────────────────────────────────────────────────────────
        // Helpers privados
        // ────────────────────────────────────────────────────────────

        private static void ValidarCamposObrigatoriosPublicacao(Projeto projeto)
        {
            if (string.IsNullOrWhiteSpace(projeto.Titulo) || projeto.Titulo.Length < 10)
                throw new NegocioException("Informe um título de pelo menos 10 caracteres.");

            if (string.IsNullOrWhiteSpace(projeto.Descricao) || projeto.Descricao.Length < 100)
                throw new NegocioException("Descreva o projeto com pelo menos 100 caracteres.");

            if (projeto.OrcamentoMin <= 0 || projeto.OrcamentoMax < projeto.OrcamentoMin)
                throw new NegocioException("Valor de orçamento inválido.");

            if (projeto.PrazoEntrega.ToUniversalTime() <= DateTime.UtcNow)
                throw new NegocioException("O prazo de entrega deve ser uma data futura.");
        }

        private static void ValidarHabilidades(List<string>? habilidades)
        {
            if (habilidades == null) return;
            if (habilidades.Count > 10)
                throw new NegocioException("Informe no máximo 10 habilidades.");
            if (habilidades.Any(h => h.Length > 50))
                throw new NegocioException("Cada habilidade deve ter no máximo 50 caracteres.");
        }

        private static List<string> DeserializarHabilidades(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static ProjetoResumoDTO MapResumo(Projeto p) => new()
        {
            Id = p.Id,
            Titulo = p.Titulo,
            DescricaoResumida = p.Descricao.Length > 200
                ? p.Descricao[..200] + "..."
                : p.Descricao,
            Categoria = p.Categoria,
            OrcamentoMin = p.OrcamentoMin,
            OrcamentoMax = p.OrcamentoMax,
            PrazoEntrega = p.PrazoEntrega,
            Habilidades = DeserializarHabilidades(p.Habilidades),
            NivelFreelancer = p.NivelFreelancer,
            TotalPropostas = p.TotalPropostas,
            PublicadoEm = p.PublicadoEm ?? p.CriadoEm,
            Status = p.Status,
            ContratanteId = p.ContratanteId,
            ContratanteNome = p.Contratante?.Nome ?? string.Empty,
            ContratanteNovo = true  // MVP: sem histórico de contratos concluídos ainda
        };

        private static ProjetoDetalheDTO MapDetalhe(Projeto p) => new()
        {
            Id = p.Id,
            Titulo = p.Titulo,
            Descricao = p.Descricao,
            Categoria = p.Categoria,
            OrcamentoMin = p.OrcamentoMin,
            OrcamentoMax = p.OrcamentoMax,
            PrazoEntrega = p.PrazoEntrega,
            Habilidades = DeserializarHabilidades(p.Habilidades),
            NivelFreelancer = p.NivelFreelancer,
            Visibilidade = p.Visibilidade,
            Idioma = p.Idioma,
            NumFreelancersDesejados = p.NumFreelancersDesejados,
            TotalPropostas = p.TotalPropostas,
            PublicadoEm = p.PublicadoEm,
            Status = p.Status,
            ContratanteId = p.ContratanteId,
            ContratanteNome = p.Contratante?.Nome ?? string.Empty,
            ContratanteLogoUrl = p.Contratante?.LogoUrl,
            ContratanteNovo = true
        };
    }
}
