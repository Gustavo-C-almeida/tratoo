using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Features.Auth
{
    public class LoginResponseDTO
    {
        public bool Sucesso { get; set; }
        public bool RequerMFA { get; set; }
        public string Mensagem { get; set; } = string.Empty;

        // Preenchidos quando Sucesso = true; usados pela API para emitir o JWT.
        public int UsuarioId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;

        /// <summary>
        /// Indica se o usuário já completou o perfil mínimo (TipoPessoa + CPF/CNPJ + Endereço).
        /// Se false, o front-end deve redirecionar para o onboarding.
        /// </summary>
        public bool PerfilMinimoCompleto { get; set; }

        /// <summary>True quando o usuário é administrador da plataforma (role Admin no JWT).</summary>
        public bool IsAdmin { get; set; }
    }
}
