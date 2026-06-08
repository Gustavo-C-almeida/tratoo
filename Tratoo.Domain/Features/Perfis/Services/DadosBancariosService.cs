using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>
    /// Gerencia os dados bancários (chave PIX) do prestador com revalidação de identidade
    /// por token de uso único enviado por e-mail. Dados sensíveis são criptografados em
    /// repouso (AES-256) e nunca retornados completos. Todas as ações são auditadas.
    /// Segue OWASP ASVS: proteção a brute force, expiração curta e escopo de propriedade.
    /// </summary>
    public class DadosBancariosService : IDadosBancariosService
    {
        private readonly IContaBancariaRepository _contaRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IEmailService _emailService;
        private readonly IAuditLogRepository _auditRepo;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DadosBancariosService> _logger;

        private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan EdicaoTtl = TimeSpan.FromMinutes(10);
        private const int MaxTentativas = 5;

        public DadosBancariosService(
            IContaBancariaRepository contaRepo,
            IPrestadorRepository prestadorRepo,
            IEmailService emailService,
            IAuditLogRepository auditRepo,
            IMemoryCache cache,
            ILogger<DadosBancariosService> logger)
        {
            _contaRepo = contaRepo;
            _prestadorRepo = prestadorRepo;
            _emailService = emailService;
            _auditRepo = auditRepo;
            _cache = cache;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER (mascarado)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<DadosBancariosViewDTO> ObterAsync(int prestadorId)
        {
            var conta = await _contaRepo.GetByPrestadorIdAsync(prestadorId);
            if (conta == null)
                return new DadosBancariosViewDTO { Configurado = false };

            return MapearMascarado(conta);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SOLICITAR ALTERAÇÃO — gera e envia token por e-mail
        // ─────────────────────────────────────────────────────────────────────────
        public async Task SolicitarAlteracaoAsync(int prestadorId, string ip)
        {
            var prestador = await _prestadorRepo.GetByIdAsync(prestadorId)
                ?? throw new NegocioException("Prestador não encontrado.");

            var token = GerarToken();
            _cache.Set(TokenKey(prestadorId), SecureHasher.Hash(token), TokenTtl);
            _cache.Remove(TentativasKey(prestadorId));
            _cache.Remove(EdicaoKey(prestadorId)); // invalida qualquer autorização anterior

            try
            {
                await _emailService.EnviarTokenDadosBancariosAsync(prestador.Email, prestador.Nome, token);
            }
            catch (Exception ex)
            {
                _cache.Remove(TokenKey(prestadorId));
                _logger.LogError(ex, "Falha ao enviar token de dados bancários ao prestador {PrestadorId}.", prestadorId);
                throw new NegocioException("Não foi possível enviar o código de confirmação. Tente novamente.");
            }

            await _auditRepo.RegistrarAsync(prestadorId, "dados_bancarios_alteracao_solicitada", ip);
            _logger.LogInformation("Token de alteração de dados bancários solicitado pelo prestador {PrestadorId}.", prestadorId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CONFIRMAR TOKEN — valida com proteção a brute force
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ConfirmarTokenAsync(int prestadorId, string token, string ip)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new NegocioException("Informe o código de confirmação.");

            if (!_cache.TryGetValue(TokenKey(prestadorId), out string? hash) || hash == null)
                throw new NegocioException("Código expirado ou inexistente. Solicite um novo.");

            if (!SecureHasher.Verify(token.Trim(), hash))
            {
                var tentativas = _cache.GetOrCreate(TentativasKey(prestadorId), e =>
                {
                    e.AbsoluteExpirationRelativeToNow = TokenTtl;
                    return 0;
                }) + 1;

                if (tentativas >= MaxTentativas)
                {
                    _cache.Remove(TokenKey(prestadorId));
                    _cache.Remove(TentativasKey(prestadorId));
                    await _auditRepo.RegistrarAsync(prestadorId, "dados_bancarios_token_bloqueado", ip);
                    _logger.LogWarning("Token de dados bancários bloqueado por brute force. Prestador {PrestadorId}.", prestadorId);
                    throw new NegocioException("Muitas tentativas inválidas. Solicite um novo código.");
                }

                _cache.Set(TentativasKey(prestadorId), tentativas, TokenTtl);
                throw new NegocioException("Código inválido.");
            }

            // Sucesso: invalida o token (uso único) e libera a janela de edição.
            _cache.Remove(TokenKey(prestadorId));
            _cache.Remove(TentativasKey(prestadorId));
            _cache.Set(EdicaoKey(prestadorId), true, EdicaoTtl);

            await _auditRepo.RegistrarAsync(prestadorId, "dados_bancarios_confirmado", ip);
            _logger.LogInformation("Token de dados bancários confirmado pelo prestador {PrestadorId}.", prestadorId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ATUALIZAR — exige confirmação de token prévia
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<DadosBancariosViewDTO> AtualizarAsync(int prestadorId, AtualizarDadosBancariosDTO dto, string ip)
        {
            // Bloqueio funcional: só salva após confirmar o token (revalidação de identidade).
            if (!_cache.TryGetValue(EdicaoKey(prestadorId), out bool autorizado) || !autorizado)
                throw new NegocioException("Confirme o código enviado por e-mail antes de salvar os dados bancários.");

            if (string.IsNullOrWhiteSpace(dto.Banco))
                throw new NegocioException("Informe o banco.");
            if (string.IsNullOrWhiteSpace(dto.Agencia))
                throw new NegocioException("Informe a agência.");
            if (string.IsNullOrWhiteSpace(dto.Conta))
                throw new NegocioException("Informe o número da conta.");

            // Valida e normaliza a chave PIX conforme o tipo.
            var chaveNormalizada = ChavePixValidator.ValidarENormalizar(dto.TipoPix, dto.ChavePix);

            var conta = await _contaRepo.GetByPrestadorIdAsync(prestadorId);
            var nova = conta == null;
            if (nova)
            {
                conta = new ContaBancaria
                {
                    PrestadorId = prestadorId,
                    CriadoEm = DateTime.UtcNow
                };
            }

            conta!.Banco = dto.Banco.Trim();
            conta.Agencia = ChavePixValidator.SomenteDigitos(dto.Agencia);
            conta.ContaCriptografada = DataProtector.Encrypt(ChavePixValidator.SomenteDigitos(dto.Conta));
            conta.TipoPix = dto.TipoPix;
            conta.PixChave = DataProtector.Encrypt(chaveNormalizada);
            conta.Ativa = true;
            conta.AtualizadoEm = DateTime.UtcNow;

            if (nova)
                await _contaRepo.AddAsync(conta);

            await _contaRepo.SaveChangesAsync();

            // Consome a autorização: cada confirmação permite uma única gravação.
            _cache.Remove(EdicaoKey(prestadorId));

            await _auditRepo.RegistrarAsync(prestadorId,
                nova ? "dados_bancarios_criado" : "dados_bancarios_atualizado", ip);
            _logger.LogInformation("Dados bancários {Acao} pelo prestador {PrestadorId}.",
                nova ? "criados" : "atualizados", prestadorId);

            return MapearMascarado(conta);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────
        private static DadosBancariosViewDTO MapearMascarado(ContaBancaria conta)
        {
            string? chaveMascarada = null;
            try
            {
                var chave = DataProtector.Decrypt(conta.PixChave);
                chaveMascarada = ChavePixValidator.Mascarar(conta.TipoPix, chave);
            }
            catch { chaveMascarada = "••••"; }

            string? contaMascarada = null;
            try
            {
                var numero = DataProtector.Decrypt(conta.ContaCriptografada);
                contaMascarada = numero.Length <= 2 ? new string('•', numero.Length)
                                                     : new string('•', numero.Length - 2) + numero[^2..];
            }
            catch { contaMascarada = "••••"; }

            return new DadosBancariosViewDTO
            {
                Configurado = true,
                Banco = conta.Banco,
                AgenciaMascarada = MascararAgencia(conta.Agencia),
                ContaMascarada = contaMascarada,
                TipoPix = conta.TipoPix,
                ChavePixMascarada = chaveMascarada,
                AtualizadoEm = conta.AtualizadoEm ?? conta.CriadoEm
            };
        }

        private static string? MascararAgencia(string? agencia)
        {
            if (string.IsNullOrEmpty(agencia)) return agencia;
            if (agencia.Length <= 1) return new string('•', agencia.Length);
            return new string('•', agencia.Length - 1) + agencia[^1];
        }

        private static string GerarToken()
            => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        private static string TokenKey(int id) => $"bancarios:token:{id}";
        private static string TentativasKey(int id) => $"bancarios:tentativas:{id}";
        private static string EdicaoKey(int id) => $"bancarios:edicao:{id}";
    }
}
