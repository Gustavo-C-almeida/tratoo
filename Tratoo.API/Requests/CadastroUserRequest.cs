using System.ComponentModel.DataAnnotations;

namespace Tratoo.API.Requests
{
    /// <summary>
    /// Etapa 1 do cadastro. CPF/CNPJ não são solicitados aqui —
    /// serão pedidos na primeira tentativa de criar proposta (Etapa 3).
    /// </summary>
    public record CadastroUserRequest(
        [property: MaxLength(100)]  string Nome,
        [property: MaxLength(254)]  string Email,
        [property: MaxLength(128)]  string Senha,
        [property: MaxLength(128)]  string ConfirmarSenha,
        bool mfa,
        [property: MaxLength(20)]   string Tipo,
        bool AceitouTermos
    );
}
