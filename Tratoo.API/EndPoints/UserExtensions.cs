using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Prestador;
using Tratoo.API.Requests;
using Tratoo.Domain.Exceptions;

namespace Tratoo.API.EndPoints
{
    public static class UserExtencions
    {
        private static CookieOptions CriarOpcoesCookie(bool isDev) => new()
        {
            HttpOnly = true,
            Secure = !isDev,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(8),
            Path = "/"
        };

        public static void AddEndPointsUsers(this WebApplication app)
        {
            var isDev = app.Environment.IsDevelopment();

            // ──────────────────────────────────────────
            // Login
            // ──────────────────────────────────────────
            app.MapPost("usuarios/login", async (
                LoginRequest request,
                HttpContext http,
                ILoginService loginService,
                IJwtService jwtService) =>
            {
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                var resultado = await loginService.AutenticarAsync(new LoginDTO
                {
                    Email = request.Email,
                    Senha = request.Senha,
                    Ip = ip
                });

                if (resultado.RequerMFA)
                    return Results.Ok(new { requerMFA = true, email = resultado.Email, mensagem = resultado.Mensagem });

                var token = jwtService.Gerar(resultado.UsuarioId, resultado.Email, resultado.Nome, resultado.Tipo, resultado.PerfilMinimoCompleto, resultado.IsAdmin);
                http.Response.Cookies.Append("tratoo_auth", token, CriarOpcoesCookie(isDev));

                return Results.Ok(new
                {
                    sucesso = true,
                    mensagem = resultado.Mensagem,
                    usuarioId = resultado.UsuarioId,
                    tipo = resultado.Tipo,
                    perfilCompleto = resultado.PerfilMinimoCompleto,
                    isAdmin = resultado.IsAdmin
                });
            }).RequireRateLimiting("login");

            // ──────────────────────────────────────────
            // Login MFA
            // ──────────────────────────────────────────
            app.MapPost("usuarios/login/mfa", async (
                ValidarLoginMFARequest request,
                HttpContext http,
                ILoginService loginService,
                IJwtService jwtService) =>
            {
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                var resultado = await loginService.ValidarMFAAsync(new ValidarLoginMFAUserDTO
                {
                    Email = request.Email,
                    Codigo = request.Codigo,
                    Ip = ip
                });

                var token = jwtService.Gerar(resultado.UsuarioId, resultado.Email, resultado.Nome, resultado.Tipo, resultado.PerfilMinimoCompleto, resultado.IsAdmin);
                http.Response.Cookies.Append("tratoo_auth", token, CriarOpcoesCookie(isDev));

                return Results.Ok(new
                {
                    sucesso = true,
                    mensagem = resultado.Mensagem,
                    usuarioId = resultado.UsuarioId,
                    tipo = resultado.Tipo,
                    perfilCompleto = resultado.PerfilMinimoCompleto,
                    isAdmin = resultado.IsAdmin
                });
            }).RequireRateLimiting("login");

            // ──────────────────────────────────────────
            // GET /api/me — dados básicos do usuário logado
            // ──────────────────────────────────────────
            app.MapGet("/api/me", (HttpContext http) =>
            {
                var userIdStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? http.User.FindFirst("sub")?.Value;
                var nome          = http.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
                var email         = http.User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
                var tipo          = http.User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
                var perfilCompleto = http.User.FindFirst("perfilCompleto")?.Value == "true";
                var isAdmin       = http.User.IsInRole("Admin");

                if (!int.TryParse(userIdStr, out var userId))
                    return Results.Unauthorized();

                return Results.Ok(new { id = userId, nome, email, tipo, perfilCompleto, isAdmin });
            }).RequireAuthorization();

            // ──────────────────────────────────────────
            // Logout
            // ──────────────────────────────────────────
            app.MapPost("usuarios/logout", (HttpContext http) =>
            {
                http.Response.Cookies.Delete("tratoo_auth", new CookieOptions { Path = "/" });
                return Results.Ok(new { mensagem = "Logout realizado com sucesso" });
            });

            // ──────────────────────────────────────────
            // Excluir conta (Soft Delete — LGPD Art. 18)
            // DELETE /usuarios/conta — Prestador ou Contratante (autenticado)
            // ──────────────────────────────────────────
            app.MapDelete("usuarios/conta", async (
                HttpContext http,
                IExclusaoContaService service) =>
            {
                var userIdStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? http.User.FindFirst("sub")?.Value;
                if (!int.TryParse(userIdStr, out var userId))
                    return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                await service.ExcluirAsync(new ExcluirContaDTO { UserId = userId, Ip = ip });

                // Encerra a sessão imediatamente — a conta não pode mais ser usada.
                http.Response.Cookies.Delete("tratoo_auth", new CookieOptions { Path = "/" });

                return Results.Ok(new { mensagem = "Conta excluída com sucesso." });
            }).RequireAuthorization();


            // ──────────────────────────────────────────
            // Etapa 1: criar conta (sem CPF/CNPJ)
            // ──────────────────────────────────────────
            app.MapPost("usuarios/cadastro", async (
                CadastroUserRequest request,
                HttpContext http,
                ICadastroService service) =>
            {
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                await service.CadastrarAsync(new CadastroUserDTO
                {
                    Nome = request.Nome,
                    Email = request.Email,
                    Senha = request.Senha,
                    ConfirmarSenha = request.ConfirmarSenha,
                    Tipo = request.Tipo,
                    MFA = request.mfa,
                    Ip = ip,
                    AceitouTermos = request.AceitouTermos
                });

                return Results.Ok(new { mensagem = "Código de verificação enviado para o e-mail informado" });
            }).RequireRateLimiting("cadastro");

            // ──────────────────────────────────────────
            // Etapa 1b: reenviar código (cooldown 1 min)
            // ──────────────────────────────────────────
            app.MapPost("usuarios/cadastro/reenviar-codigo", async (
                ReenviarCodigoRequest request,
                ICadastroService service) =>
            {
                await service.ReenviarCodigoAsync(request.Email);
                return Results.Ok(new { mensagem = "Novo código de verificação enviado para o e-mail informado" });
            }).RequireRateLimiting("cadastro");

            // ──────────────────────────────────────────
            // Etapa 2: confirmar e-mail
            // ──────────────────────────────────────────
            app.MapPost("usuarios/cadastro/confirmar", async (
                ConfirmarCadastroRequest request,
                ICadastroService service) =>
            {
                await service.ConfirmarCadastroAsync(new ConfirmarCadastroUserDTO
                {
                    Email = request.Email,
                    Codigo = request.Codigo
                });

                return Results.Ok(new { mensagem = "Cadastro confirmado com sucesso" });
            });

            // ──────────────────────────────────────────
            // Esqueci minha senha — Etapa 1: solicitar código
            // ──────────────────────────────────────────
            app.MapPost("usuarios/senha/resetar/solicitar", async (
                SolicitarResetSenhaRequest request,
                HttpContext http,
                ILoginService loginService) =>
            {
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                await loginService.SolicitarResetSenhaAsync(request.Email, ip);
                // Resposta idêntica independentemente de o e-mail existir (anti-enumeração)
                return Results.Ok(new
                {
                    mensagem = "Se o e-mail estiver cadastrado, você receberá um código de redefinição em até 1 minuto."
                });
            }).RequireRateLimiting("senha");

            // ──────────────────────────────────────────
            // Esqueci minha senha — Etapa 2: confirmar código e definir nova senha
            // ──────────────────────────────────────────
            app.MapPost("usuarios/senha/resetar", async (
                ResetarSenhaRequest request,
                HttpContext http,
                ILoginService loginService) =>
            {
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                await loginService.ResetarSenhaAsync(new ResetarSenhaDTO
                {
                    Email = request.Email,
                    Codigo = request.Codigo,
                    NovaSenha = request.NovaSenha,
                    ConfirmarNovaSenha = request.ConfirmarNovaSenha,
                    Ip = ip
                });
                return Results.Ok(new
                {
                    mensagem = "Senha redefinida com sucesso. Você já pode fazer login com a nova senha."
                });
            }).RequireRateLimiting("senha");

            // ──────────────────────────────────────────
            // Onboarding: completar perfil mínimo pós-login
            // (TipoPessoa + CPF/CNPJ + Endereço)
            // ──────────────────────────────────────────
            app.MapPost("usuarios/onboarding", async (
                OnboardingRequest request,
                HttpContext http,
                IUsuarioRepository usuarioRepo,
                IIdentidadeService identidadeService,
                IJwtService jwtService) =>
            {
                var userIdStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? http.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
                    return Results.Unauthorized();

                var usuario = await usuarioRepo.ObterPorIdAsync(userId)
                    ?? throw new Tratoo.Domain.Exceptions.NegocioException("Usuário não encontrado");

                if (!Enum.TryParse<TipoPessoa>(request.TipoPessoa, ignoreCase: true, out var tipoPessoa))
                    throw new Tratoo.Domain.Exceptions.NegocioException("Tipo de pessoa inválido. Use 'PessoaFisica' ou 'PessoaJuridica'.");

                // TipoPessoa e Endereço são compartilhados — ficam em Usuario
                usuario.TipoPessoa = tipoPessoa;
                usuario.Endereco = new Tratoo.Domain.Models.Endereco
                {
                    Cep         = request.Cep,
                    Logradouro  = request.Logradouro,
                    Numero      = request.Numero,
                    Complemento = request.Complemento,
                    Bairro      = request.Bairro,
                    Cidade      = request.Cidade,
                    Estado      = request.Estado
                };

                // Dados específicos de cada tipo
                if (usuario is Tratoo.Domain.Models.Contratante contratante)
                {
                    if (tipoPessoa == TipoPessoa.PessoaJuridica)
                    {
                        if (string.IsNullOrWhiteSpace(request.Segmento))
                            throw new Tratoo.Domain.Exceptions.NegocioException("Segmento de atuação é obrigatório para Pessoa Jurídica.");

                        contratante.Segmento = request.Segmento;
                        contratante.NomeEmpresa = request.NomeEmpresa;
                        contratante.InscricaoEstadual = request.InscricaoEstadual;
                        contratante.InscricaoMunicipal = request.InscricaoMunicipal;
                        contratante.DataAbertura = request.DataAbertura;
                    }
                    else
                    {
                        contratante.ExibirIdade = request.ExibirIdade;
                    }
                }

                await usuarioRepo.AtualizarAsync(usuario);

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                // Valida e persiste CPF/CNPJ (e representante legal para PJ)
                // Também chama VerificarPerfilMinimo() e salva o usuário
                await identidadeService.ValidarEPersistirAsync(new ValidarIdentidadeDTO
                {
                    UserId = userId,
                    CpfCnpj = request.CpfCnpj,
                    NomeLegal = request.NomeLegal,
                    CpfRepresentanteLegal = request.CpfRepresentanteLegal,
                    NomeRepresentanteLegal = request.NomeRepresentanteLegal,
                    CargoRepresentanteLegal = request.CargoRepresentante,
                    EmailRepresentanteLegal = request.EmailRepresentante,
                    TelefoneRepresentanteLegal = request.TelefoneRepresentante,
                    DataNascimento = request.DataNascimento,
                    ExibirIdade = request.ExibirIdade,
                    Ip = ip
                });

                // Recarrega para obter o status atualizado (PerfilMinimoCompleto pode ter mudado)
                var usuarioAtualizado = await usuarioRepo.ObterPorIdAsync(userId);
                var perfilCompleto = usuarioAtualizado?.PerfilMinimoCompleto ?? false;

                // Re-emite o cookie JWT com perfilCompleto=true para desbloquear imediatamente
                // todas as rotas protegidas sem exigir um novo login.
                if (perfilCompleto)
                {
                    var novoToken = jwtService.Gerar(
                        usuarioAtualizado!.Id,
                        usuarioAtualizado.Email,
                        usuarioAtualizado.Nome,
                        usuarioAtualizado.TipoUsuario.ToString(),
                        perfilMinimoCompleto: true);

                    http.Response.Cookies.Append("tratoo_auth", novoToken, CriarOpcoesCookie(isDev));
                }

                return Results.Ok(new
                {
                    mensagem = "Perfil completado com sucesso",
                    perfilCompleto
                });
            }).RequireAuthorization();
        }
    }
}
