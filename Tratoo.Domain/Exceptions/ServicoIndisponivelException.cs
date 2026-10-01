namespace Tratoo.Domain.Exceptions
{
    /// <summary>
    /// Uma dependência necessária para concluir a operação com segurança está fora do ar
    /// (ex.: o Redis que guarda os códigos OTP). A API responde 503 com Retry-After — a
    /// operação NÃO é feita de forma degradada: validar um código sem o armazenamento seria
    /// aceitar qualquer código.
    /// </summary>
    public class ServicoIndisponivelException : Exception
    {
        public ServicoIndisponivelException(string mensagem, Exception? causa = null)
            : base(mensagem, causa) { }
    }
}
