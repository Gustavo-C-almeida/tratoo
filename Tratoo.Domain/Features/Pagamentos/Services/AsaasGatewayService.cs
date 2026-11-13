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
                        "Asaas [tentativa {T}/{Max}] erro ao obter QR Code para {CobrancaId}: {Status}, {Resumo}",
                        tentativa, maxTentativas, cobrancaId, resp.StatusCode, AsaasIntegracaoException.ResumirResposta(body));

                    if (tentativa == maxTentativas)
                        throw new InvalidOperationException(
                            $"Erro ao obter QR Code PIX após {maxTentativas} tentativas: {resp.StatusCode} — {AsaasIntegracaoException.ResumirResposta(body)}");

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
                        expiration = DatasUtc.Instante(dt);
                }

                // Somente valida se obteve o payload mínimo
                if (string.IsNullOrEmpty(payload))
                {
                    _logger.LogWarning(
                        "Asaas [tentativa {T}/{Max}] QR Code disponível mas payload vazio para {CobrancaId}. Resposta com {Bytes} bytes.",
                        tentativa, maxTentativas, cobrancaId, body.Length);

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
            var body = new Dictionary<string, object?>
            {
                ["value"]             = (double)request.Valor,
                ["pixAddressKey"]     = request.ChavePix,
                ["pixAddressKeyType"] = request.TipoChavePix,
                ["description"]       = request.Descricao
            };
            if (!string.IsNullOrWhiteSpace(request.ReferenciaExterna))
                body["externalReference"] = request.ReferenciaExterna;

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
                // Falha de rede ou timeout: o pedido pode ter chegado e a transferência pode
                // ter sido criada. Resultado INCERTO — nunca repetir sem conciliar.
                _logger.LogError(ex,
                    "Timeout ou falha de rede ao criar transferência PIX (resultado incerto). Valor: R$ {Valor}, Tipo: {Tipo}, Chave: {Chave}",
                    request.Valor, request.TipoChavePix, MascararChave(request.ChavePix));

                throw new AsaasIntegracaoException(
                    HttpStatusCode.ServiceUnavailable,
                    ex.Message,
                    AsaasErroTipo.ResultadoIncerto,
                    $"Falha de rede/timeout ao contactar o Asaas: {ex.Message}");
            }

            if (!httpResp.IsSuccessStatusCode)
            {
                var tipoErro = AsaasIntegracaoException.ClassificarTransferencia(httpResp.StatusCode, responseBody);
                // Só códigos e tamanho: o corpo pode ecoar chave PIX e dados do favorecido.
                var resumo = AsaasIntegracaoException.ResumirResposta(responseBody);

                if (tipoErro == AsaasErroTipo.ResultadoIncerto)
                {
                    // NÃO é uma rejeição: o status não comprova que nada foi criado.
                    _logger.LogError(
                        "Resposta inconclusiva do Asaas à criação da transferência PIX (resultado incerto). " +
                        "HTTP {StatusCode}, {Resumo}. Valor: R$ {Valor}, TipoPix: {TipoPix}, Chave: {Chave}",
                        (int)httpResp.StatusCode, resumo,
                        request.Valor, request.TipoChavePix, MascararChave(request.ChavePix));
                }
                else
                {
                    _logger.LogError(
                        "Asaas recusou a criação da transferência PIX (nada foi criado). " +
                        "HTTP {StatusCode}, Tipo: {TipoErro}, {Resumo}. Valor: R$ {Valor}, TipoPix: {TipoPix}, Chave: {Chave}",
                        (int)httpResp.StatusCode, tipoErro, resumo,
                        request.Valor, request.TipoChavePix, MascararChave(request.ChavePix));
                }

                throw new AsaasIntegracaoException(
                    httpResp.StatusCode,
                    responseBody,
                    tipoErro,
                    tipoErro == AsaasErroTipo.ResultadoIncerto
                        ? $"Asaas [{(int)httpResp.StatusCode}] sem resposta conclusiva: {resumo}"
                        : $"Asaas [{(int)httpResp.StatusCode}] recusou: {resumo}");
            }

            // 2xx: o Asaas aceitou o pedido. Se não der para ler o id, a transferência
            // provavelmente EXISTE — resultado incerto, nunca "falha recuperável".
            string id;
            string status;
            try
            {
                var responseNode = JsonNode.Parse(responseBody)
                    ?? throw new InvalidOperationException("Resposta vazia do Asaas.");
                id = responseNode["id"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Asaas não retornou ID da transferência.");
                status = responseNode["status"]?.GetValue<string>() ?? "PENDING";
            }
            catch (Exception ex)
            {
                // Sem o corpo no log: ele traz a chave PIX sem máscara.
                _logger.LogError(ex,
                    "Resposta {StatusCode} do Asaas sem ID de transferência legível (resultado incerto). Valor: R$ {Valor}",
                    (int)httpResp.StatusCode, request.Valor);
                throw new AsaasIntegracaoException(
                    httpResp.StatusCode,
                    string.Empty,
                    AsaasErroTipo.ResultadoIncerto,
                    $"Asaas respondeu {(int)httpResp.StatusCode}, mas sem ID de transferência legível: {ex.Message}");
            }

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

            HttpResponseMessage resp;
            string respBody;
            try
            {
                resp = await _http.PostAsync($"payments/{Uri.EscapeDataString(cobrancaId)}/refund", content);
                respBody = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                // O pedido pode ter chegado e o estorno pode ter sido feito: resultado INCERTO.
                _logger.LogError(ex, "Timeout ou falha de rede ao estornar cobrança {CobrancaId} (resultado incerto).", cobrancaId);
                throw new AsaasIntegracaoException(
                    HttpStatusCode.ServiceUnavailable, string.Empty, AsaasErroTipo.ResultadoIncerto,
                    $"Falha de rede/timeout ao estornar no Asaas: {ex.Message}");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var tipoErro = AsaasIntegracaoException.ClassificarEstorno(resp.StatusCode, respBody);
                // Só códigos e tamanho: o corpo pode ecoar dados do pagador.
                var resumo = AsaasIntegracaoException.ResumirResposta(respBody);
                if (tipoErro == AsaasErroTipo.ResultadoIncerto)
                    _logger.LogError("Resposta inconclusiva do Asaas ao estorno da cobrança {CobrancaId} (resultado incerto): {Status}, {Resumo}",
                        cobrancaId, (int)resp.StatusCode, resumo);
                else
                    _logger.LogError("Asaas recusou o estorno da cobrança {CobrancaId} (nada foi estornado): {Status}, {Resumo}",
                        cobrancaId, (int)resp.StatusCode, resumo);

                throw new AsaasIntegracaoException(resp.StatusCode, respBody, tipoErro,
                    tipoErro == AsaasErroTipo.ResultadoIncerto
                        ? $"Erro ao processar estorno: Asaas [{(int)resp.StatusCode}] sem resposta conclusiva: {resumo}"
                        : $"Erro ao processar estorno: Asaas [{(int)resp.StatusCode}] recusou: {resumo}");
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
                    "Falha ao simular pagamento para cobrança {CobrancaId}: {Status}, {Resumo}",
                    cobrancaId, resp.StatusCode, AsaasIntegracaoException.ResumirResposta(respBody));
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
                var resumo = AsaasIntegracaoException.ResumirResposta(responseBody);
                _logger.LogError(
                    "Asaas API error {Status} em POST {Endpoint}: {Resumo}",
                    response.StatusCode, endpoint, resumo);

                // A descrição do Asaas vai para o usuário que fez a operação (ex.: "CPF
                // inválido"). Sem descrição, nunca o corpo bruto: só o resumo.
                string mensagem;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    var erros = doc.RootElement.TryGetProperty("errors", out var e) ? e : default;
                    mensagem = erros.ValueKind == JsonValueKind.Array && erros.GetArrayLength() > 0
                        && erros[0].TryGetProperty("description", out var d) && d.GetString() is { } desc
                        ? desc
                        : resumo;
                }
                catch
                {
                    mensagem = resumo;
                }

                throw new InvalidOperationException($"Asaas API [{response.StatusCode}]: {mensagem}");
            }

            return JsonNode.Parse(responseBody)
                ?? throw new InvalidOperationException("Resposta vazia do Asaas.");
        }
    }
}
