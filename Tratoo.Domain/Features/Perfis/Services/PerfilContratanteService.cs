using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Perfis
{
    public class PerfilContratanteService
    {
        private readonly IContratanteRepository _repo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly IAvaliacaoRepository _avaliacaoRepo;
        private readonly IArquivoStorageService _storage;

        public PerfilContratanteService(
            IContratanteRepository repo,
            IIdentidadeRepository identidadeRepo,
            IAvaliacaoRepository avaliacaoRepo,
            IArquivoStorageService storage)
        {
            _repo = repo;
            _identidadeRepo = identidadeRepo;
            _avaliacaoRepo = avaliacaoRepo;
            _storage = storage;
        }

        public async Task<PerfilPublicoContratanteDTO> PerfilPublicoAsync(int id)
        {
            var c = await _repo.GetByIdAsync(id)
                ?? throw new NegocioException("Contratante não encontrado");

            var identity = await _identidadeRepo.ObterPorUserIdAsync(id);

            int? idade = null;
            if (identity is not null && c.ExibirIdade && identity.DataNascimento.HasValue)
                idade = CalcularIdade(identity.DataNascimento.Value);

            var (totalProjetos, concluidos, valorMedio) = await _repo.GetMetricasProjetosAsync(id);
            var reputacao = await _avaliacaoRepo.GetReputacaoAsync(id);
            var ultimosProjetos = await _repo.GetUltimosProjetosAsync(id, 5);

            var taxaConclusao = totalProjetos > 0
                ? (int)Math.Round((double)concluidos / totalProjetos * 100)
                : 0;

            var empresaVerificada = identity is not null &&
                identity.NivelVerificacao >= NivelVerificacao.Identidade;

            return new PerfilPublicoContratanteDTO
            {
                Id = c.Id,
                Nome = c.Nome,
                Descricao = c.Descricao,
                LogoUrl = c.LogoUrl,
                SiteUrl = c.SiteUrl,
                LinkedinUrl = c.LinkedinUrl,
                Segmento = c.Segmento,
                NomeEmpresa = c.NomeEmpresa,
                LocalizacaoCidade = c.Endereco?.Cidade,
                LocalizacaoEstado = c.Endereco?.Estado,
                Idade = idade,
                CriadoEm = c.DataCadastro,
                TotalProjetosPublicados = totalProjetos,
                TotalProjetosConcluidos = concluidos,
                TaxaConclusao = taxaConclusao,
                MediaAvaliacoes = reputacao?.MediaGeral,
                TotalAvaliacoes = reputacao?.TotalAvaliacoes ?? 0,
                ValorMedioProjetos = valorMedio,
                EmpresaVerificada = empresaVerificada,
                UltimosProjetos = ultimosProjetos.Select(p => new ProjetoResumoPerfilDto
                {
                    Id           = p.Id,
                    Titulo       = p.Titulo,
                    Descricao    = p.Descricao,
                    Status       = p.Status.ToString(),
                    OrcamentoMin = p.OrcamentoMin,
                    OrcamentoMax = p.OrcamentoMax,
                    PublicadoEm  = p.PublicadoEm,
                    PrazoEntrega = p.PrazoEntrega,
                    TotalPropostas = p.TotalPropostas
                }).ToList()
            };
        }

        public async Task<MeuPerfilContratanteDTO> MeuPerfilAsync(int id)
        {
            var c = await _repo.GetByIdAsync(id)
                ?? throw new NegocioException("Contratante não encontrado");

            var identity = await _identidadeRepo.ObterPorUserIdAsync(id);

            int? idade = null;
            if (identity?.DataNascimento.HasValue == true)
                idade = CalcularIdade(identity.DataNascimento!.Value);

            var (porcentagem, proximoPasso) = CalcularCompletude(c);

            return new MeuPerfilContratanteDTO
            {
                Id = c.Id,
                Nome = c.Nome,
                Descricao = c.Descricao,
                LogoUrl = c.LogoUrl,
                SiteUrl = c.SiteUrl,
                LinkedinUrl = c.LinkedinUrl,
                Telefone = c.Telefone,
                Segmento = c.Segmento,
                NomeEmpresa = c.NomeEmpresa,
                InscricaoEstadual = c.InscricaoEstadual,
                InscricaoMunicipal = c.InscricaoMunicipal,
                DataAbertura = c.DataAbertura,
                LocalizacaoCidade = c.Endereco?.Cidade,
                LocalizacaoEstado = c.Endereco?.Estado,
                ExibirIdade = c.ExibirIdade,
                Idade = idade,
                TipoPessoa = c.TipoPessoa?.ToString(),
                AvaliacoesPrivado = c.AvaliacoesPrivado,
                PorcentagemCompletude = porcentagem,
                ProximoPassoCompletude = proximoPasso
            };
        }

        public async Task AtualizarPerfilAsync(AtualizarPerfilContratanteDTO dto)
        {
            var c = await _repo.GetByIdAsync(dto.ContratanteId)
                ?? throw new NegocioException("Contratante não encontrado");

            if (dto.Descricao is not null && dto.Descricao.Length > 1000)
                throw new NegocioException("Descrição muito longa. Limite: 1.000 caracteres.");

            if (!string.IsNullOrWhiteSpace(dto.SiteUrl) && !dto.SiteUrl.StartsWith("https://"))
                throw new NegocioException("URL do site inválida. Use o formato https://...");

            if (!string.IsNullOrWhiteSpace(dto.LinkedinUrl))
            {
                if (!dto.LinkedinUrl.StartsWith("https://") ||
                    !dto.LinkedinUrl.Contains("linkedin.com"))
                    throw new NegocioException("URL do LinkedIn inválida. Use o formato https://linkedin.com/...");
            }

            c.Descricao = dto.Descricao;
            c.SiteUrl = dto.SiteUrl;
            c.LinkedinUrl = dto.LinkedinUrl;
            c.Telefone = dto.Telefone;
            c.ExibirIdade = dto.ExibirIdade;

            c.VerificarPerfilMinimo();
            await _repo.UpdateAsync(c);
            await _repo.SaveAsync();
        }

        public async Task<string> AtualizarFotoAsync(int id, Stream conteudo, string nomeArquivo, string contentType)
        {
            var c = await _repo.GetByIdAsync(id)
                ?? throw new NegocioException("Contratante não encontrado");

            if (!string.IsNullOrWhiteSpace(c.LogoUrl))
                await _storage.ExcluirAsync(c.LogoUrl);

            var ext = Path.GetExtension(nomeArquivo).ToLowerInvariant();
            var chave = $"contratante/fotos/{id}_{Guid.NewGuid()}{ext}";
            var url = await _storage.UploadAsync(conteudo, chave, contentType);

            c.LogoUrl = url;
            await _repo.UpdateAsync(c);
            await _repo.SaveAsync();

            return url;
        }

        public async Task RemoverFotoAsync(int id)
        {
            var c = await _repo.GetByIdAsync(id)
                ?? throw new NegocioException("Contratante não encontrado");

            if (!string.IsNullOrWhiteSpace(c.LogoUrl))
            {
                await _storage.ExcluirAsync(c.LogoUrl);
                c.LogoUrl = null;
                await _repo.UpdateAsync(c);
                await _repo.SaveAsync();
            }
        }

        /// <summary>
        /// Calcula a percentagem de completude do perfil do contratante (0–100)
        /// e sugere o próximo passo mais impactante.
        /// Pontuação: LogoUrl=15, Descricao>=50=20, LinkedinUrl=10, SiteUrl=10,
        ///            NomeEmpresa ou Segmento=20, Cidade=15, Telefone=10.
        /// </summary>
        public static (int Porcentagem, string? ProximoPasso) CalcularCompletude(Contratante c)
        {
            int score = 0;
            string? proximoPasso = null;

            if (!string.IsNullOrWhiteSpace(c.LogoUrl))
                score += 15;
            else
                proximoPasso ??= "Adicione uma foto ou logo para transmitir mais credibilidade (+15%)";

            if (!string.IsNullOrWhiteSpace(c.Descricao) && c.Descricao.Length >= 50)
                score += 20;
            else
                proximoPasso ??= "Escreva uma bio com pelo menos 50 caracteres (+20%)";

            if (!string.IsNullOrWhiteSpace(c.LinkedinUrl))
                score += 10;
            else
                proximoPasso ??= "Adicione seu LinkedIn — contratantes com LinkedIn recebem 3× mais propostas de qualidade (+10%)";

            if (!string.IsNullOrWhiteSpace(c.SiteUrl))
                score += 10;
            else
                proximoPasso ??= "Adicione o site da empresa (+10%)";

            if (!string.IsNullOrWhiteSpace(c.NomeEmpresa) || !string.IsNullOrWhiteSpace(c.Segmento))
                score += 20;
            else
                proximoPasso ??= "Informe o nome da empresa ou segmento de atuação (+20%)";

            if (!string.IsNullOrWhiteSpace(c.Endereco?.Cidade))
                score += 15;
            else
                proximoPasso ??= "Informe sua cidade para aparecer em buscas regionais (+15%)";

            if (!string.IsNullOrWhiteSpace(c.Telefone))
                score += 10;
            else
                proximoPasso ??= "Adicione um telefone de contato (+10%)";

            return (score, proximoPasso);
        }

        private static int CalcularIdade(DateOnly nascimento)
        {
            var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
            var idade = hoje.Year - nascimento.Year;
            if (nascimento > hoje.AddYears(-idade)) idade--;
            return idade;
        }
    }
}
