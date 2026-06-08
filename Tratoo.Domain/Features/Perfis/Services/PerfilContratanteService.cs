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

            // Conta excluída (Soft Delete — LGPD): perfil não é mais acessível
            if (c.ExcluidoEm != null)
                throw new NegocioException("Contratante não encontrado");

            var identity = await _identidadeRepo.ObterPorUserIdAsync(id);

            int? idade = null;
            if (identity is not null && c.ExibirIdade && identity.DataNascimento.HasValue)
                idade = CalcularIdade(identity.DataNascimento.Value);

            var (totalProjetos, concluidos, valorMedio) = await _repo.GetMetricasProjetosAsync(id);
            var (projetosAtivos, contratosConcluidoss, tempoMedioDecisao) = await _repo.GetMetricasAdicionaisAsync(id);
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
                EmailContato = c.EmailContato,
                Segmento = c.Segmento,
                NomeEmpresa = c.NomeEmpresa,
                LocalizacaoCidade = c.Endereco?.Cidade,
                LocalizacaoEstado = c.Endereco?.Estado,
                Idade = idade,
                CriadoEm = c.DataCadastro,
                TipoPessoa = c.TipoPessoa?.ToString(),
                EmpresaVerificada = empresaVerificada,
                PagadorVerificado = c.PagadorVerificado,
                AnoAbertura = c.DataAbertura?.Year,
                TamanhoEquipe = c.TamanhoEquipe?.ToString(),
                IdiomasAceitos = c.GetIdiomasAceitos(),
                Disponibilidade = c.Disponibilidade?.ToString(),
                PorQueTrabalharComigo = c.PorQueTrabalharComigo,
                TotalProjetosPublicados = totalProjetos,
                TotalProjetosConcluidos = concluidos,
                TotalProjetosAtivos = projetosAtivos,
                TotalContratosConcluidoss = contratosConcluidoss,
                TaxaConclusao = taxaConclusao,
                MediaAvaliacoes = reputacao?.MediaGeral,
                TotalAvaliacoes = reputacao?.TotalAvaliacoes ?? 0,
                ValorMedioProjetos = valorMedio,
                TempoMedioDecisaoDias = tempoMedioDecisao.HasValue
                    ? Math.Round(tempoMedioDecisao.Value, 1)
                    : null,
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
                EmailContato = c.EmailContato,
                Telefone = c.Telefone,
                Segmento = c.Segmento,
                NomeEmpresa = c.NomeEmpresa,
                DataAbertura = c.DataAbertura,
                LocalizacaoCidade = c.Endereco?.Cidade,
                LocalizacaoEstado = c.Endereco?.Estado,
                ExibirIdade = c.ExibirIdade,
                Idade = idade,
                TipoPessoa = c.TipoPessoa?.ToString(),
                AvaliacoesPrivado = c.AvaliacoesPrivado,
                Disponibilidade = c.Disponibilidade?.ToString(),
                IdiomasAceitos = c.GetIdiomasAceitos(),
                TamanhoEquipe = c.TamanhoEquipe?.ToString(),
                PorQueTrabalharComigo = c.PorQueTrabalharComigo,
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

            if (!string.IsNullOrWhiteSpace(dto.EmailContato) &&
                !dto.EmailContato.Contains('@'))
                throw new NegocioException("E-mail de contato inválido.");

            if (dto.PorQueTrabalharComigo is not null && dto.PorQueTrabalharComigo.Length > 500)
                throw new NegocioException("Seção 'Por que trabalhar comigo' muito longa. Limite: 500 caracteres.");

            c.Descricao = dto.Descricao;
            c.SiteUrl = dto.SiteUrl;
            c.LinkedinUrl = dto.LinkedinUrl;
            c.EmailContato = string.IsNullOrWhiteSpace(dto.EmailContato) ? null : dto.EmailContato.Trim();
            c.Telefone = dto.Telefone;
            c.ExibirIdade = dto.ExibirIdade;
            c.PorQueTrabalharComigo = string.IsNullOrWhiteSpace(dto.PorQueTrabalharComigo) ? null : dto.PorQueTrabalharComigo.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Segmento))
                c.Segmento = dto.Segmento.Trim();

            if (!string.IsNullOrWhiteSpace(dto.NomeEmpresa))
                c.NomeEmpresa = dto.NomeEmpresa.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Disponibilidade) &&
                Enum.TryParse<DisponibilidadeContratante>(dto.Disponibilidade, out var disp))
                c.Disponibilidade = disp;
            else if (dto.Disponibilidade == null)
                c.Disponibilidade = null;

            if (!string.IsNullOrWhiteSpace(dto.TamanhoEquipe) &&
                Enum.TryParse<TamanhoEquipe>(dto.TamanhoEquipe, out var tam))
                c.TamanhoEquipe = tam;
            else if (dto.TamanhoEquipe == null)
                c.TamanhoEquipe = null;

            if (dto.IdiomasAceitos is not null)
                c.SetIdiomasAceitos(dto.IdiomasAceitos);

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
        /// Calcula a percentagem de completude do perfil do contratante (0–100).
        /// Pontuação: LogoUrl=10, Descricao>=50=15, LinkedinUrl=8, SiteUrl=8,
        ///            NomeEmpresa ou Segmento=15, Cidade=10, Telefone=8,
        ///            EmailContato=8, PorQueTrabalharComigo=8, Disponibilidade=5,
        ///            IdiomasAceitos=5. Total=100.
        /// </summary>
        public static (int Porcentagem, string? ProximoPasso) CalcularCompletude(Contratante c)
        {
            int score = 0;
            string? proximoPasso = null;

            if (!string.IsNullOrWhiteSpace(c.LogoUrl))
                score += 10;
            else
                proximoPasso ??= "Adicione uma foto ou logo para transmitir mais credibilidade (+10%)";

            if (!string.IsNullOrWhiteSpace(c.Descricao) && c.Descricao.Length >= 50)
                score += 15;
            else
                proximoPasso ??= "Escreva uma bio com pelo menos 50 caracteres (+15%)";

            if (!string.IsNullOrWhiteSpace(c.NomeEmpresa) || !string.IsNullOrWhiteSpace(c.Segmento))
                score += 15;
            else
                proximoPasso ??= "Informe o nome da empresa ou segmento de atuação (+15%)";

            if (!string.IsNullOrWhiteSpace(c.Endereco?.Cidade))
                score += 10;
            else
                proximoPasso ??= "Informe sua cidade para aparecer em buscas regionais (+10%)";

            if (!string.IsNullOrWhiteSpace(c.LinkedinUrl))
                score += 8;
            else
                proximoPasso ??= "Adicione seu LinkedIn — contratantes com LinkedIn recebem propostas de maior qualidade (+8%)";

            if (!string.IsNullOrWhiteSpace(c.SiteUrl))
                score += 8;
            else
                proximoPasso ??= "Adicione o site da empresa (+8%)";

            if (!string.IsNullOrWhiteSpace(c.Telefone))
                score += 8;
            else
                proximoPasso ??= "Adicione um telefone de contato (+8%)";

            if (!string.IsNullOrWhiteSpace(c.EmailContato))
                score += 8;
            else
                proximoPasso ??= "Adicione um e-mail de contato público (+8%)";

            if (!string.IsNullOrWhiteSpace(c.PorQueTrabalharComigo))
                score += 8;
            else
                proximoPasso ??= "Preencha 'Por que trabalhar comigo' para atrair melhores prestadores (+8%)";

            if (c.Disponibilidade.HasValue)
                score += 5;
            else
                proximoPasso ??= "Informe sua disponibilidade para novos prestadores (+5%)";

            if (!string.IsNullOrWhiteSpace(c.IdiomasAceitosJson))
                score += 5;
            else
                proximoPasso ??= "Informe os idiomas aceitos nos projetos (+5%)";

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
