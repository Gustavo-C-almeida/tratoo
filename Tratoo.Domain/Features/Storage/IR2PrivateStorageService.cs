namespace Tratoo.Domain.Features.Storage
{
    public interface IR2PrivateStorageService
    {
        /// <summary>
        /// Faz upload de um arquivo no bucket privado e retorna a chave do objeto (não uma URL pública).
        /// </summary>
        Task<string> UploadAsync(Stream conteudo, string chaveArquivo, string contentType);

        /// <summary>
        /// Gera uma URL pré-assinada temporária para acesso ao objeto privado.
        /// </summary>
        Task<string> GerarUrlAssinadaAsync(string chaveArquivo, TimeSpan validade);

        /// <summary>
        /// Remove um objeto do bucket privado pela sua chave.
        /// </summary>
        Task ExcluirAsync(string chaveArquivo);
    }
}
