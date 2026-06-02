namespace Tratoo.Domain.Features.Storage
{
    public interface IArquivoStorageService
    {
        /// <summary>Faz upload de um arquivo e retorna a URL pública.</summary>
        Task<string> UploadAsync(Stream conteudo, string nomeArquivo, string contentType);

        /// <summary>Remove um arquivo pelo caminho/chave (não pela URL completa).</summary>
        Task ExcluirAsync(string chaveArquivo);
    }
}
