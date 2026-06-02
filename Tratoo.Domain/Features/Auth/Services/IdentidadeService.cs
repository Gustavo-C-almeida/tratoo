using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Auth
{
    public class IdentidadeService : IIdentidadeService
    {
        private readonly IUsuarioRepository _usuarioRepo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly HttpClient _httpClient;

        public IdentidadeService(
            IUsuarioRepository usuarioRepo,
            IIdentidadeRepository identidadeRepo,
            HttpClient httpClient)
        {
            _usuarioRepo = usuarioRepo;
            _identidadeRepo = identidadeRepo;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Valida o CPF ou CNPJ (localmente) e salva em UserIdentity.
        /// Lança NegocioException se inválido ou se o documento já está em outra conta.
        /// </summary>
        public async Task ValidarEPersistirAsync(ValidarIdentidadeDTO dto)
        {
            var usuario = await _usuarioRepo.ObterPorIdAsync(dto.UserId)
                ?? throw new NegocioException("Usuário não encontrado");

            if (usuario.Status != StatusUsuario.Active)
                throw new NegocioException("Conta inativa ou bloqueada");

            // Verifica se já tem identidade validada
            var existente = await _identidadeRepo.ObterPorUserIdAsync(dto.UserId);
            if (existente is not null)
                throw new NegocioException("Identidade já validada para esta conta");

            var docLimpo = LimparDocumento(dto.CpfCnpj);

            if (docLimpo.Length == 11)
                await ValidarCpfAsync(docLimpo);
            else if (docLimpo.Length == 14)
                await ValidarCNPJAsync(docLimpo);
            else
                throw new NegocioException("CPF deve ter 11 dígitos e CNPJ 14 dígitos");

            var docCriptografado = DataProtector.Encrypt(docLimpo);

            // Anti-fraude: um documento não pode estar em duas contas ativas
            if (await _identidadeRepo.CpfCnpjJaExisteAsync(docCriptografado))
                throw new NegocioException("Documento já vinculado a outra conta");

            // Para PF: exige data de nascimento
            if (docLimpo.Length == 11)
            {
                if (dto.DataNascimento is null)
                    throw new NegocioException("Data de nascimento é obrigatória para Pessoa Física.");
            }

            // Para PJ: valida e criptografa o CPF do representante legal
            string? cpfRepCriptografado = null;
            if (docLimpo.Length == 14)
            {
                if (string.IsNullOrWhiteSpace(dto.CpfRepresentanteLegal) || string.IsNullOrWhiteSpace(dto.NomeRepresentanteLegal))
                    throw new NegocioException("CPF e nome do representante legal são obrigatórios para Pessoa Jurídica.");

                var cpfRepLimpo = LimparDocumento(dto.CpfRepresentanteLegal);
                await ValidarCpfAsync(cpfRepLimpo);
                cpfRepCriptografado = DataProtector.Encrypt(cpfRepLimpo);
            }

            var identity = new UserIdentity
            {
                UserId = dto.UserId,
                CpfCnpjCriptografado = docCriptografado,
                NomeLegal = dto.NomeLegal,
                NivelVerificacao = NivelVerificacao.Identidade,
                // PJ
                CpfRepresentanteLegalCriptografado = cpfRepCriptografado,
                NomeRepresentanteLegal = dto.NomeRepresentanteLegal,
                CargoRepresentanteLegal = dto.CargoRepresentanteLegal,
                EmailRepresentanteLegal = dto.EmailRepresentanteLegal,
                TelefoneRepresentanteLegal = dto.TelefoneRepresentanteLegal,
                // PF
                DataNascimento = dto.DataNascimento,
                ExibirIdade = dto.ExibirIdade,
                CriadoEm = DateTime.UtcNow
            };

            await _identidadeRepo.SalvarAsync(identity);

            // Atualiza flag de identidade verificada e recalcula perfil mínimo
            usuario.IdentidadeVerificada = true;
            usuario.VerificarPerfilMinimo();
            await _usuarioRepo.AtualizarAsync(usuario);
        }

        // ──────────────────────────────────────────────
        // CPF — validação local pelo algoritmo dos dígitos verificadores
        // ──────────────────────────────────────────────
        private static Task ValidarCpfAsync(string cpf)
        {
            if (!ValidadorCpf.Validar(cpf))
                throw new NegocioException("CPF inválido");

            return Task.CompletedTask;
        }

        // ──────────────────────────────────────────────
        // CNPJ — BrasilAPI GET /api/cnpj/v1/{cnpj}
        // ──────────────────────────────────────────────
        private static Task ValidarCNPJAsync(string cnpj)
        {
            if (!ValidadorCNPJ.Validar(cnpj))
                throw new NegocioException("CPF inválido");

            return Task.CompletedTask;
        }

        private static string LimparDocumento(string doc)
            => Regex.Replace(doc ?? string.Empty, @"\D", "");

        // Mapeamento mínimo da resposta da BrasilAPI
        private record BrasilApiCnpjResponse(string Situacao);
    }
}
