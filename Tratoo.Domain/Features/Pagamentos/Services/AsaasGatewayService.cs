using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tratoo.Domain.Features.Pagamentos
{
    /// <summary>
    /// Implementação concreta do gateway de 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// 
    /// usando a API Asaas v3.
    /// Usa HttpClient tipado para comunicação e trata erros da API de forma explícita.
    /// Para trocar de gateway, basta criar outra implementação de IAsaasGatewayService.
    /// </summary>
    public class AsaasGatewayService : IAsaasGatewayService
    {
        private readonly HttpClient _http;
        private readonly AsaasConfig _config;
        private readonly ILogger<AsaasGatewayService> _logger;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public AsaasGatewayService(
            HttpClient http,
            IOptions<AsaasConfig> config,
            ILogger<AsaasGatewayService> logger)
        {
            _http = http;
            _config = config.Value;
            _logger = logger;

            _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Add("access_token", _config.ApiKey);
            _http.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CLIENTE
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<string> CriarOuObterClienteAsync(AsaasClienteRequest request)
        {
            // Tenta buscar cliente já existente pela referência externa
            var externalRef = $"user-{request.UsuarioId}";
            var existente = await BuscarClientePorExternalRefAsync(externalRef);
            if (existente != null)
            {
                _logger.LogDebug("Cliente Asaas reutilizado: {ClienteId} para usuário {UsuarioId}", existente, request.UsuarioId);
                return existente;
            }

            // Cria novo cliente
            var body = new
            {
                name = request.Nome,
                cpfCnpj = request.CpfCnpj.Replace(".", "").Replace("-", "").Replace("/", ""),
                email = request.Email,
                externalReference = externalRef,
                notificationDisabled = true   // evita spam do Asaas ao cliente
            };

            var response = await PostAsync("/customers", body);
            var id = response["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Asaas não retornou ID do cliente.");

            _logger.LogInformation("Cliente Asaas criado: {ClienteId} para usuário {UsuarioId}", id, request.UsuarioId);
            return id;
        }

        private async Task<string?> BuscarClientePorExternalRefAsync(string externalRef)
        {
            try
            {
                var resp = await _http.GetAsync($"customers?externalReference={Uri.EscapeDataString(externalRef)}&limit=1");
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.GetArrayLength() > 0)
                {
                    return data[0].TryGetProperty("id", out var idProp)
                        ? idProp.GetString()
                        : null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao buscar cliente Asaas por externalReference {Ref}", externalRef);
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // COBRANÇA PIX
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<AsaasCobrancaResponse> CriarCobrancaPixAsync(AsaasCobrancaRequest request)
        {
            var body = new
            {
                customer = request.AsaasClienteId,
                billingType = "PIX",
                value = (double)request.Valor,
                dueDate = request.DataVencimento.ToString("yyyy-MM-dd"),
                description = request.Descricao,
                externalReference = request.ReferenciaExterna
            };

            var response = await PostAsync("/payments", body);

            var id = response["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Asaas não retornou ID da cobrança.");
            var status = response["status"]?.GetValue<string>() ?? "PENDING";
            var invoiceUrl = response["invoiceUrl"]?.GetValue<string>();

            _logger.LogInformation(
                "Cobrança PIX criada no Asaas: {CobrancaId}, valor R$ {Valor}, ref: {Ref}",
                id, request.Valor, request.ReferenciaExterna);

            return new AsaasCobrancaResponse(id, status, invoiceUrl);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // QR CODE PIX
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<AsaasPixQrCodeResponse> ObterQrCodePixAsync(string cobrancaId)
        {
            var resp = await _http.GetAsync($"payments/{Uri.EscapeDataString(cobrancaId)}/pixQrCode");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Asaas retornou erro ao obter QR Code para {CobrancaId}: {Body}", cobrancaId, body);
                throw new InvalidOperationException($"Erro ao obter QR Code PIX: {resp.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var payload = root.TryGetProperty("payload", out var p) ? p.GetString() ?? "" : "";
            var image = root.TryGetProperty("encodedImage", out var img) ? img.GetString() : null;
            DateTime? expiration = null;

            if (root.TryGetProperty("expirationDate", out var exp) && exp.ValueKind != JsonValueKind.Null)
            {
                if (exp.TryGetDateTime(out var dt))
                    expiration = dt;
            }

            return new AsaasPixQrCodeResponse(payload, image, expiration);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // TRANSFERÊNCIA PIX
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<AsaasTransferenciaResponse> CriarTransferenciaPixAsync(AsaasTransferenciaPixRequest request)
        {
            var body = new
            {
                value = (double)request.Valor,
                pixAddressKey = request.ChavePix,
                pixAddressKeyType = request.TipoChavePix,
                description = request.Descricao
            };

            var response = await PostAsync("/transfers", body);

            var id = response["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Asaas não retornou ID da transferência.");
            var status = response["status"]?.GetValue<string>() ?? "PENDING";

            _logger.LogInformation(
                "Transferência PIX criada no Asaas: {TransId}, valor R$ {Valor}, chave: {Chave}",
                id, request.Valor, request.ChavePix);

            return new AsaasTransferenciaResponse(id, status);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ESTORNO
        // ─────────────────────────────────────────────────────────────────────────
        public async Task EstornarCobrancaAsync(string cobrancaId, decimal? valorParcial = null)
        {
            object body = valorParcial.HasValue
                ? new { value = (double)valorParcial.Value }
                : new { };

            var json = JsonSerializer.Serialize(body, _jsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"payments/{Uri.EscapeDataString(cobrancaId)}/refund", content);
            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Falha ao estornar cobrança {CobrancaId} no Asaas: {Body}", cobrancaId, respBody);
                throw new InvalidOperationException($"Erro ao processar estorno: {resp.StatusCode} - {respBody}");
            }

            _logger.LogInformation("Estorno solicitado para cobrança Asaas: {CobrancaId}", cobrancaId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // STATUS DA COBRANÇA
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<string> ObterStatusCobrancaAsync(string cobrancaId)
        {
            var resp = await _http.GetAsync($"payments/{Uri.EscapeDataString(cobrancaId)}/status");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return "UNKNOWN";

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("status", out var s)
                ? s.GetString() ?? "UNKNOWN"
                : "UNKNOWN";
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SIMULAÇÃO (sandbox/localhost apenas)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task SimularPagamentoAsync(string cobrancaId, decimal valor)
        {
            var body = new
            {
                paymentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                value = (double)valor,
                notifyCustomer = false
            };

            var json = JsonSerializer.Serialize(body, _jsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(
                $"payments/{Uri.EscapeDataString(cobrancaId)}/receiveInCash", content);
            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Falha ao simular pagamento para cobrança {CobrancaId}: {Body}",
                    cobrancaId, respBody);
                throw new InvalidOperationException(
                    $"Não foi possível simular o pagamento no Asaas Sandbox: {resp.StatusCode}");
            }

            _logger.LogInformation(
                "Pagamento simulado com sucesso para cobrança {CobrancaId}, valor R$ {Valor}",
                cobrancaId, valor);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers privados
        // ─────────────────────────────────────────────────────────────────────────
        private async Task<JsonNode> PostAsync(string endpoint, object body)
        {
            var json = JsonSerializer.Serialize(body, _jsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(endpoint.TrimStart('/'), content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Asaas API error {Status} em POST {Endpoint}: {Body}",
                    response.StatusCode, endpoint, responseBody);

                // Tenta extrair mensagem de erro do Asaas
                string mensagem;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    var erros = doc.RootElement.TryGetProperty("errors", out var e) ? e : default;
                    mensagem = erros.ValueKind == JsonValueKind.Array && erros.GetArrayLength() > 0
                        ? erros[0].TryGetProperty("description", out var d) ? d.GetString() ?? responseBody : responseBody
                        : responseBody;
                }
                catch
                {
                    mensagem = responseBody;
                }

                throw new InvalidOperationException($"Asaas API [{response.StatusCode}]: {mensagem}");
            }

            return JsonNode.Parse(responseBody)
                ?? throw new InvalidOperationException("Resposta vazia do Asaas.");
        }
    }
}
