namespace Tratoo.Domain.Enums
{
    public enum StatusUsuario
    {
        /// <summary>Aguardando confirmação de e-mail</summary>
        Pending,

        /// <summary>E-mail confirmado — pode fazer login</summary>
        Active,

        /// <summary>Conta bloqueada ou excluída (LGPD)</summary>
        Blocked
    }
}
