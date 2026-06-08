using System.Net;
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
            _http.DefaultRequestHeaders.Add("User-Agent", "Tratoo/1.0");
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
        // A Asaas processa o QR Code de forma assíncrona após criar o pagamento.
        // Fazemos até 4 tentativas com 1,5 s de espera entre elas para garantir
        // que o código esteja disponível antes de desistir.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<AsaasPixQrCodeResponse> ObterQrCodePixAsync(string cobrancaId)
        {
            const int maxTentativas = 4;
            const int delayMs = 1500;

            for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
            {
                // Aguarda antes de cada tentativa (inclusive a primeira, para dar
                // tempo ao Asaas de processar o QR Code após criar o pagamento).
                await Task.Delay(delayMs);

                var resp = await _http.GetAsync($"payments/{Uri.EscapeDataString(cobrancaId)}/pixQrCode");
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Asaas [tentativa {T}/{Max}] erro ao obter QR Code para {CobrancaId}: {Status} {Body}",
                        tentativa, maxTentativas, cobrancaId, resp.StatusCode, body);

                    if (tentativa == maxTentativas)
                        throw new InvalidOperationException(
                            $"Erro ao obter QR Code PIX após {maxTentativas} tentativas: {resp.StatusCode} — {body}");

                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var payload = root.TryGetProperty("payload", out var p) ? p.GetString() ?? "" : "";

                // Asaas retorna base64 puro — o browser precisa do prefixo data URI
                // para renderizar via <img src="...">.
                string? image = null;
                if (root.TryGetProperty("encodedImage", out var img) && img.ValueKind == JsonValueKind.String)
                {
                    var raw = img.GetString();
                    if (!string.IsNullOrEmpty(raw))
                        image = raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                            ? raw
                            : $"data:image/png;base64,{raw}";
                }

                DateTime? expiration = null;
                if (root.TryGetProperty("expirationDate", out var exp) && exp.ValueKind != JsonValueKind.Null)
                {
                    // Asaas retorna offset brasileiro (-03:00); ToUniversalTime normaliza.
                    if (exp.TryGetDateTimeOffset(out var dto))
                        expiration = dto.UtcDateTime;
                    else if (exp.TryGetDateTime(out var dt))
                        expiration = dt;
                }

                // Somente valida se obteve o payload mínimo
                if (string.IsNullOrEmpty(payload))
                {
                    _logger.LogWarning(
                        "Asaas [tentativa {T}/{Max}] QR Code disponível mas payload vazio para {CobrancaId}. Resposta: {Body}",
                        tentativa, maxTentativas, cobrancaId, body);

                    if (tentativa < maxTentativas) continue;
                }

                _logger.LogInformation(
                    "QR Code PIX obtido com sucesso para {CobrancaId} na tentativa {T}. Payload: {Len} chars.",
                    cobrancaId, tentativa, payload.Length);

                return new AsaasPixQrCodeResponse(payload, image, expiration);
            }

            // Nunca atingido (o loop lança na última tentativa com erro HTTP),
            // mas necessário para o compilador.
            throw new InvalidOperationException($"Não foi possível obter QR Code PIX para {cobrancaId}.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // TRANSFERÊNCIA PIX
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<AsaasTransferenciaResponse> CriarTransferenciaPixAsync(AsaasTransferenciaPixRequest request)
        {
            var body = new
            {
                value           = (double)request.Valor,
                pixAddressKey   = request.ChavePix,
                pixAddressKeyType = request.TipoChavePix,
                description     = request.Descricao
            };

            var json    = JsonSerializer.Serialize(body, _jsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage httpResp;
            string              responseBody;
            try
            {
                httpResp     = await _http.PostAsync("transfers", content);
                responseBody = await httpResp.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                // Falha de rede ou timeout — gateway pode estar processando, não marcar como falha definitiva.
                _logger.LogError(ex,
                    "Timeout ou falha de rede ao criar transferência PIX. Valor: R$ {Valor}, Tipo: {Tipo}, Chave: {Chave}",
                    request.Valor, request.TipoChavePix, MascararChave(request.ChavePix));

                throw new AsaasIntegracaoException(
                    HttpStatusCode.ServiceUnavailable,
                    ex.Message,
                    AsaasErroTipo.GatewayIndisponivel,
                    $"Falha de rede/timeout ao contactar o Asaas: {ex.Message}");
            }

            if (!httpResp.IsSuccessStatusCode)
            {
                var tipoErro = AsaasIntegracaoException.Classificar(httpResp.StatusCode, responseBody);

                _logger.LogError(
                    "Asaas rejeitou a transferência PIX. Status: {StatusCode}, Tipo: {TipoErro}, " +
                    "Valor: R$ {Valor}, TipoPix: {TipoPix}, Chave: {Chave}, Resposta: {Body}",
                    (int)httpResp.StatusCode, tipoErro,
                    request.Valor, request.TipoChavePix, MascararChave(request.ChavePix),
                    responseBody);

                // Extrai a primeira mensagem de erro do Asaas para a exceção
                string mensagemErro;
                try
                {
                    using var doc  = JsonDocument.Parse(responseBody);
                    var erros      = doc.RootElement.TryGetProperty("errors", out var e) ? e : default;
                    mensagemErro   = erros.ValueKind == JsonValueKind.Array && erros.GetArrayLength() > 0
                        ? erros[0].TryGetProperty("description", out var d) ? d.GetString() ?? responseBody : responseBody
                        : responseBody;
                }
                catch
                {
                    mensagemErro = responseBody;
                }

                throw new AsaasIntegracaoException(
                    httpResp.StatusCode,
                    responseBody,
                    tipoErro,
                    $"Asaas [{(int)httpResp.StatusCode}]: {mensagemErro}");
            }

            JsonNode responseNode;
            try
            {
                responseNode = JsonNode.Parse(responseBody)
                    ?? throw new InvalidOperationException("Resposta vazia do Asaas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao deserializar resposta de transferência do Asaas. Body: {Body}", responseBody);
                throw;
            }

            var id     = responseNode["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Asaas não retornou ID da transferência.");
            var status = responseNode["status"]?.GetValue<string>() ?? "PENDING";

            _logger.LogInformation(
                "Transferência PIX criada no Asaas: {TransId}, status: {StatusAsaas}, " +
                "valor R$ {Valor}, tipo {Tipo}, chave: {Chave}",
                id, status, request.Valor, request.TipoChavePix, MascararChave(request.ChavePix));

            return new AsaasTransferenciaResponse(id, status);
        }

        // Mascara a chave PIX para logs — preserva apenas os últimos 4 caracteres.
        private static string MascararChave(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return "****";
            return chave.Length <= 4 ? new string('*', chave.Length) : new string('*', chave.Length - 4) + chave[^4..];
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
