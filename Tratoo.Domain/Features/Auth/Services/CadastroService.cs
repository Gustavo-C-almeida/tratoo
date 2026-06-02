using System.Net.Mail;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Auth
{
    public class CadastroService : ICadastroService
    {
        private readonly IUsuarioRepository _repo;
        private readonly IEmailService _emailService;
        private readonly IVerificacaoMFAService _mfaService;
        private readonly ICacheTempService _cacheTemp;
        private readonly IConsentLogRepository _consentRepo;

        // Regra: token de confirmação de e-mail válido por 24h
        private static readonly TimeSpan ExpiracaoCadastro = TimeSpan.FromHours(24);

        // Cooldown de 1 minuto entre reenvios
        private static readonly TimeSpan CooldownReenvio = TimeSpan.FromMinutes(1);

        private const string VersaoTermos = "v1.0";

        public CadastroService(
            IUsuarioRepository repo,
            IEmailService emailService,
            IVerificacaoMFAService mfaService,
            ICacheTempService cacheTemp,
            IConsentLogRepository consentRepo)
        {
            _repo = repo;
            _emailService = emailService;
            _mfaService = mfaService;
            _cacheTemp = cacheTemp;
            _consentRepo = consentRepo;
        }

        /// <summary>
        /// Etapa 1: recebe dados básicos,
        /// e envia código de confirmação por e-mail.
        /// </summary>
        public async Task CadastrarAsync(CadastroUserDTO dto)
        {
            // Normaliza antes de qualquer validação ou acesso ao cache/banco
            dto.Email = dto.Email.Trim().ToLowerInvariant();

            CadastroValidator.Validar(dto);

            if (!dto.AceitouTermos)
                throw new NegocioException("É necessário aceitar os Termos de Uso e a Política de Privacidade para criar uma conta.");

            if (await _repo.EmailExisteAsync(dto.Email))
                throw new NegocioException("E-mail já cadastrado");

            // Task.Run evita que o BCrypt (CPU-intensivo, ~300ms) bloqueie a thread da requisição
            var senhaHash = await Task.Run(() => PasswordHasher.Hash(dto.Senha));

            var dadosPendentes = new DadosCadastroPendente(
                Nome: dto.Nome,
                Email: dto.Email,
                Tipo: dto.Tipo,
                MFA: dto.MFA,
                SenhaHash: senhaHash,
                Ip: dto.Ip,
                AceitouTermos: dto.AceitouTermos);

            string codigo = _mfaService.GerarECriar(dto.Email, "cadastro");

            _cacheTemp.Salvar(
                ChaveCadastro(dto.Email),
                dadosPendentes,
                ExpiracaoCadastro);

            try
            {
                await _emailService.EnviarCodigoVerificacaoAsync(dto.Email, codigo);
            }
            catch (SmtpException)
            {
                _cacheTemp.Remover(ChaveCadastro(dto.Email));
                throw new NegocioException("Não foi possível enviar o e-mail de verificação. Tente novamente.");
            }
            catch (TimeoutException)
            {
                _cacheTemp.Remover(ChaveCadastro(dto.Email));
                throw new NegocioException("Tempo excedido ao enviar o e-mail de verificação. Tente novamente.");
            }
            catch
            {
                _cacheTemp.Remover(ChaveCadastro(dto.Email));
                throw;
            }
        }

        /// valida o código de e-mail, cria o usuário com Status=Active e grava os ConsentLogs
        public async Task ConfirmarCadastroAsync(ConfirmarCadastroUserDTO dto)
        {
            var email = dto.Email.Trim().ToLowerInvariant();

            // Valida o código e já o remove do cache (uso único)
            _mfaService.Validar(email, dto.Codigo, "cadastro");

            var dados = _cacheTemp.Obter<DadosCadastroPendente>(ChaveCadastro(email));

            if (dados is null)
                throw new NegocioException("Cadastro expirado. Inicie o processo novamente.");

            var usuario = UsuarioFactory.Criar(dados);

            await _repo.SalvarAsync(usuario);

            // LGPD Art. 7: registra o aceite dos termos e da política de privacidade
            // Um único roundtrip ao banco para os dois registros
            await _consentRepo.SalvarLoteAsync(new[]
            {
                new ConsentLog { UserId = usuario.Id, Tipo = TipoConsentimento.Termos,      Versao = VersaoTermos, Ip = dados.Ip },
                new ConsentLog { UserId = usuario.Id, Tipo = TipoConsentimento.Privacidade, Versao = VersaoTermos, Ip = dados.Ip }
            });

            _cacheTemp.Remover(ChaveCadastro(email));
        }

        /// <summary>
        /// Reenvia o código de verificação para um cadastro pendente,
        /// bloqueando novas tentativas durante 1 minuto.
        /// </summary>
        public async Task ReenviarCodigoAsync(string email)
        {
            email = email.Trim().ToLowerInvariant();

            var chaveCooldown = ChaveCooldownReenvio(email);

            if (_cacheTemp.Obter<bool?>(chaveCooldown) is not null)
                throw new NegocioException("Aguarde 1 minuto antes de solicitar um novo código.");

            var dados = _cacheTemp.Obter<DadosCadastroPendente>(ChaveCadastro(email));

            if (dados is null)
                throw new NegocioException("Cadastro expirado ou não encontrado. Inicie o processo novamente.");

            string codigo = _mfaService.GerarECriar(email, "cadastro");

            // Registra o cooldown antes de enviar para evitar disparos duplos em caso de retry
            _cacheTemp.Salvar(chaveCooldown, true, CooldownReenvio);

            try
            {
                await _emailService.EnviarCodigoVerificacaoAsync(email, codigo);
            }
            catch
            {
                _cacheTemp.Remover(chaveCooldown);
                throw;
            }
        }

        private static string ChaveCadastro(string email) => $"cadastro:{email}";
        private static string ChaveCooldownReenvio(string email) => $"cadastro:cooldown-reenvio:{email}";
    }
}
