using System.Diagnostics;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public class LoginService : ILoginService
    {
        private readonly IUsuarioRepository _repo;
        private readonly IAuditLogRepository _auditRepo;
        private readonly IEmailService _emailService;
        private readonly IVerificacaoMFAService _mfaService;

        public LoginService(
            IUsuarioRepository repo,
            IAuditLogRepository auditRepo,
            IEmailService emailService,
            IVerificacaoMFAService mfaService)
        {
            _repo = repo;
            _auditRepo = auditRepo;
            _emailService = emailService;
            _mfaService = mfaService;
        }

        public async Task<LoginResponseDTO> AutenticarAsync(LoginDTO dto)
        {
            LoginValidator.Validar(dto.Email, dto.Senha);

            var sw = Stopwatch.StartNew();
            var usuario = await _repo.ObterPorEmailAsync(dto.Email);

            // Sempre executa BCrypt — com DummyHash quando o usuário não existe.
            // Isso normaliza o tempo de CPU independentemente de o e-mail estar cadastrado,
            // eliminando o timing side-channel que revelaria enumeração de e-mails.
            var hashParaVerificar = usuario?.SenhaHash ?? PasswordHasher.DummyHash;
            bool senhaValida = PasswordHasher.Verify(dto.Senha, hashParaVerificar) && usuario != null;

            // Garante mínimo de 300 ms em todos os caminhos (redes rápidas, cache hits, etc.).
            var restante = 300 - (int)sw.ElapsedMilliseconds;
            if (restante > 0)
                await Task.Delay(restante);

            if (!senhaValida)
                throw new NegocioException("Usuário ou senha inválidos");

            if (usuario!.Status == StatusUsuario.Pending)
                throw new NegocioException("Confirme seu e-mail antes de fazer login.");

            if (usuario.Status == StatusUsuario.Blocked)
                throw new NegocioException("Conta bloqueada. Entre em contato com o suporte.");

            if (usuario.MFA)
            {
                string codigo = _mfaService.GerarECriar(usuario.Email, "login");
                await _emailService.EnviarCodigoVerificacaoAsync(usuario.Email, codigo);

                return new LoginResponseDTO
                {
                    RequerMFA = true,
                    Email = usuario.Email,
                    Mensagem = "Código de verificação enviado para o seu e-mail"
                };
            }

            await _auditRepo.RegistrarAsync(usuario.Id, "login", dto.Ip);

            return new LoginResponseDTO
            {
                Sucesso = true,
                Mensagem = "Login realizado com sucesso",
                UsuarioId = usuario.Id,
                Nome = usuario.Nome,
                Email = usuario.Email,
                Tipo = usuario.TipoUsuario.ToString(),
                PerfilMinimoCompleto = usuario.PerfilMinimoCompleto,
                IsAdmin = usuario.IsAdmin
            };
        }

        public async Task<LoginResponseDTO> ValidarMFAAsync(ValidarLoginMFAUserDTO dto)
        {
            var usuario = await _repo.ObterPorEmailAsync(dto.Email)
                ?? throw new NegocioException("Usuário não encontrado");

            if (!usuario.MFA)
                throw new NegocioException("MFA não habilitado para este usuário");

            _mfaService.Validar(dto.Email, dto.Codigo, "login");

            await _auditRepo.RegistrarAsync(usuario.Id, "login_mfa", dto.Ip);

            return new LoginResponseDTO
            {
                Sucesso = true,
                Mensagem = "Login realizado com sucesso",
                UsuarioId = usuario.Id,
                Nome = usuario.Nome,
                Email = usuario.Email,
                Tipo = usuario.TipoUsuario.ToString(),
                PerfilMinimoCompleto = usuario.PerfilMinimoCompleto,
                IsAdmin = usuario.IsAdmin
            };
        }

        /// <summary>
        /// Inicia o fluxo de redefinição de senha enviando um código de 6 dígitos
        /// para o e-mail do usuário. Sempre retorna sem erro — mesmo que o e-mail
        /// não exista — para não revelar quais contas estão cadastradas.
        /// </summary>
        public async Task SolicitarResetSenhaAsync(string email, string ip)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new NegocioException("E-mail obrigatório.");

            var usuario = await _repo.ObterPorEmailAsync(email.Trim());

            // Só envia o código se a conta existe e está ativa.
            // A resposta HTTP é idêntica nos dois casos (anti-enumeração).
            if (usuario is { Status: StatusUsuario.Active })
            {
                string codigo = _mfaService.GerarECriar(usuario.Email, "reset_senha");
                await _emailService.EnviarCodigoResetSenhaAsync(usuario.Email, codigo);
                await _auditRepo.RegistrarAsync(usuario.Id, "reset_senha_solicitado", ip);
            }
        }

        /// <summary>
        /// Conclui o fluxo de redefinição: valida o código OTP (elimina-o do cache),
        /// aplica a nova senha com BCrypt e registra o evento de auditoria.
        /// </summary>
        public async Task ResetarSenhaAsync(ResetarSenhaDTO dto)
        {
            // Valida regras de senha antes de qualquer I/O
            LoginValidator.ValidarNovaSenha(dto.NovaSenha, dto.ConfirmarNovaSenha);

            // Erro genérico: não revela se o e-mail existe nem se o código era inválido
            const string erroPadrao = "Código inválido ou expirado.";

            var usuario = await _repo.ObterPorEmailAsync(dto.Email.Trim())
                ?? throw new NegocioException(erroPadrao);

            if (usuario.Status != StatusUsuario.Active)
                throw new NegocioException(erroPadrao);

            // Valida o OTP e remove do cache (uso único garantido)
            _mfaService.Validar(usuario.Email, dto.Codigo, "reset_senha");

            usuario.SenhaHash = PasswordHasher.Hash(dto.NovaSenha);
            await _repo.AtualizarAsync(usuario);

            await _auditRepo.RegistrarAsync(usuario.Id, "reset_senha_concluido", dto.Ip);
        }
    }
}
