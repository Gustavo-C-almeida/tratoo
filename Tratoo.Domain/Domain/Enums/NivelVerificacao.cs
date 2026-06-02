namespace Tratoo.Domain.Enums
{
    public enum NivelVerificacao
    {
        /// <summary>E-mail verificado: pode navegar, mas não pode negociar</summary>
        Basico = 1,

        /// <summary>CPF/CNPJ validado: pode criar propostas e assinar contratos</summary>
        Identidade = 2,

        /// <summary>Dados bancários cadastrados: pode receber repasses (só prestadores)</summary>
        Financeiro = 3
    }
}
