namespace Tratoo.Domain.Exceptions
{
    public class NegocioException : Exception
    {
        public NegocioException(string mensagem) : base(mensagem) { }
    }
}
