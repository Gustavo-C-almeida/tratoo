using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>
    /// Visão mascarada dos dados bancários do prestador. NUNCA expõe a chave PIX,
    /// o número da conta ou qualquer dado sensível completo (OWASP ASVS V8).
    /// </summary>
    public class DadosBancariosViewDTO
    {
        /// <summary>True quando já existe conta bancária cadastrada.</summary>
        public bool Configurado { get; set; }

        public string? Banco { get; set; }
        public string? AgenciaMascarada { get; set; }
        public string? ContaMascarada { get; set; }

        public TipoPix? TipoPix { get; set; }
        /// <summary>Chave PIX parcialmente mascarada (ex: "***.***.789-01", "j***@e***.com").</summary>
        public string? ChavePixMascarada { get; set; }

        public DateTime? AtualizadoEm { get; set; }
    }

    /// <summary>Entrada para criar/atualizar os dados bancários (exige confirmação por token).</summary>
    public class AtualizarDadosBancariosDTO
    {
        public string Banco { get; set; } = string.Empty;
        public string Agencia { get; set; } = string.Empty;
        public string Conta { get; set; } = string.Empty;
        public TipoPix TipoPix { get; set; }
        public string ChavePix { get; set; } = string.Empty;
    }

    /// <summary>Entrada para confirmar o token de revalidação enviado por e-mail.</summary>
    public class ConfirmarTokenBancarioDTO
    {
        public string Token { get; set; } = string.Empty;
    }
}
