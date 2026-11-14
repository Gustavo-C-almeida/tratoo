namespace Tratoo.Domain.Exceptions
{
    /// <summary>
    /// A operação perdeu uma disputa concorrente pelo mesmo recurso (ex.: a liberação manual de um
    /// pagamento que outra operação reivindicou primeiro). A API responde 409 Conflict: não é erro
    /// de validação do pedido — o estado mudou, e o cliente deve recarregá-lo. Herda de
    /// <see cref="NegocioException"/> para quem já trata regra de negócio continuar tratando.
    /// </summary>
    public class ConflitoException : NegocioException
    {
        public ConflitoException(string mensagem) : base(mensagem) { }
    }
}
