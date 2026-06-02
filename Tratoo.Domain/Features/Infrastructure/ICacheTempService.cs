namespace Tratoo.Domain.Features.Infrastructure
{
    public interface ICacheTempService
    {
        void Salvar<T>(string chave, T dados, TimeSpan expiracao);
        T? Obter<T>(string chave);
        void Remover(string chave);
    }
}
