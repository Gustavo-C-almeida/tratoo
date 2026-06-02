using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tratoo.Domain.Features.Storage
{
    public class R2PrivateConfig
    {
        public string AccountId  { get; set; } = string.Empty;
        public string AccessKey  { get; set; } = string.Empty;
        public string SecretKey  { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string ServiceURL { get; set; } = string.Empty;
    }

    public class R2PrivateStorageService : IR2PrivateStorageService
    {
        private readonly R2PrivateConfig _cfg;
        private readonly ILogger<R2PrivateStorageService> _logger;

        public R2PrivateStorageService(IOptions<R2PrivateConfig> cfg, ILogger<R2PrivateStorageService> logger)
        {
            _cfg = cfg.Value;
            _logger = logger;
        }

        private IAmazonS3 CriarCliente()
        {
            var serviceUrl = string.IsNullOrWhiteSpace(_cfg.ServiceURL)
                ? $"https://{_cfg.AccountId}.r2.cloudflarestorage.com"
                : _cfg.ServiceURL.TrimEnd('/');

            var s3Config = new AmazonS3Config
            {
                ServiceURL       = serviceUrl,
                ForcePathStyle   = true,
                AuthenticationRegion = "auto"
            };

            var credentials = new BasicAWSCredentials(_cfg.AccessKey, _cfg.SecretKey);
            return new AmazonS3Client(credentials, s3Config);
        }

        public async Task<string> UploadAsync(Stream conteudo, string chaveArquivo, string contentType)
        {
            try
            {
                using var s3 = CriarCliente();

                using var memoryStream = new MemoryStream();
                await conteudo.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var request = new PutObjectRequest
                {
                    BucketName = _cfg.BucketName,
                    Key = chaveArquivo,
                    InputStream = memoryStream,
                    ContentType = contentType,
                    Headers = { ContentLength = memoryStream.Length },
                    UseChunkEncoding = false
                };

                var response = await s3.PutObjectAsync(request);

                _logger.LogDebug("R2 upload concluído. Key={Key}, StatusCode={StatusCode}, RequestId={RequestId}",
                    chaveArquivo, response.HttpStatusCode, response.ResponseMetadata.RequestId);

                return chaveArquivo;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex,
                    "Erro S3 ao fazer upload. Key={Key}, StatusCode={StatusCode}, ErrorCode={ErrorCode}, RequestId={RequestId}",
                    chaveArquivo, ex.StatusCode, ex.ErrorCode, ex.RequestId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao fazer upload para R2. Key={Key}", chaveArquivo);
                throw;
            }
        }

        public Task<string> GerarUrlAssinadaAsync(string chaveArquivo, TimeSpan validade)
        {
            using var s3 = CriarCliente();

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _cfg.BucketName,
                Key        = chaveArquivo,
                Expires    = DateTime.UtcNow.Add(validade),
                Verb       = HttpVerb.GET,
                Protocol   = Protocol.HTTPS
            };

            var url = s3.GetPreSignedURL(request);
            return Task.FromResult(url);
        }

        public async Task ExcluirAsync(string chaveArquivo)
        {
            if (string.IsNullOrWhiteSpace(chaveArquivo)) return;
            using var s3 = CriarCliente();
            await s3.DeleteObjectAsync(_cfg.BucketName, chaveArquivo);
        }
    }
}
