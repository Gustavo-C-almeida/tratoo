namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Etapa 1 do cadastro: apenas dados básicos de conta.
    /// CPF/CNPJ NÃO são solicitados aqui — serão pedidos somente
    /// na primeira vez que o usuário tentar criar uma proposta (Etapa 3).
    /// </summary>
    public class CadastroUserDTO
    {
        public string Nome { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public string ConfirmarSenha { get; set; } = string.Empty;

        /// <summary>PRESTADOR ou CONTRATANTE</summary>
        public string Tipo { get; set; } = string.Empty;

        public bool MFA { get; set; }

        /// <summary>IP do cliente — preenchido pela camada de API/UI antes de chamar o service.</summary>
        public string Ip { get; set; } = string.Empty;

        /// <summary>Obrigatório: usuário deve aceitar Termos de Uso e Política de Privacidade.</summary>
        public bool AceitouTermos { get; set; }
    }
}
