using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;

namespace Tratoo.Domain.Features.Storage
{
    public class R2Config
    {
        public string AccountId  { get; set; } = string.Empty;
        public string AccessKey  { get; set; } = string.Empty;
        public string SecretKey  { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string PublicUrl  { get; set; } = string.Empty;
    }

    public class R2StorageService : IArquivoStorageService
    {
        private readonly R2Config _cfg;

        public R2StorageService(IOptions<R2Config> cfg)
        {
            var accountid = cfg.Value.AccountId;
            _cfg = cfg.Value;
        }



        private IAmazonS3 CriarCliente()
        {
            var endpoint = $"https://{_cfg.AccountId}.r2.cloudflarestorage.com";

            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            };

            var credentials = new BasicAWSCredentials(_cfg.AccessKey, _cfg.SecretKey);
            return new AmazonS3Client(credentials, s3Config);
        }

        public async Task<string> UploadAsync(Stream conteudo, string nomeArquivo, string contentType)
        {
            using var s3 = CriarCliente();
            var nomeSeguro = $"{Guid.NewGuid()}_{Path.GetFileName(nomeArquivo)}";

            // Copia o stream para MemoryStream para garantir que o comprimento é conhecido
            using var memoryStream = new MemoryStream();
            await conteudo.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var request = new PutObjectRequest
            {
                BucketName = _cfg.BucketName,
                Key = nomeSeguro,
                InputStream = memoryStream,
                ContentType = contentType,
                Headers = { ContentLength = memoryStream.Length },
                UseChunkEncoding = false
            };

            await s3.PutObjectAsync(request);

            return $"{_cfg.PublicUrl.TrimEnd('/')}/{nomeSeguro}";
        }

        public async Task ExcluirAsync(string chaveArquivo)
        {
            if (string.IsNullOrWhiteSpace(chaveArquivo)) return;

            // Se receber a URL pública completa, extrai só a chave
            var chave = chaveArquivo.StartsWith(_cfg.PublicUrl)
                ? chaveArquivo[$"{_cfg.PublicUrl.TrimEnd('/')}/".Length..]
                : chaveArquivo;

            using var s3 = CriarCliente();
            await s3.DeleteObjectAsync(_cfg.BucketName, chave);
        }
    }
}
