using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Fase 4 da auditoria — rate limiting medido com requisições reais contra a API (duas
    /// réplicas, contador no Redis). Cada teste usa IPs de cliente inéditos.
    ///
    /// Fora daqui, já cobertos por testes existentes: Redis fora do ar → degrada para o limite
    /// em memória (EstadoEfemeroTests), políticas não compartilham balde (RateLimitingPorIpTests).
    /// </summary>
    [Collection(ColecaoApiReal.Nome)]
    public sealed class RateLimitTests
    {
        private readonly ApiRealFixture _f;
        public RateLimitTests(ApiRealFixture f) => _f = f;

        private static readonly Dictionary<string, int> Limites = new()
        {
            ["login"] = 10, ["cadastro"] = 5, ["senha"] = 3, ["otp-assinatura"] = 3,
            ["dados-bancarios"] = 5, ["refresh"] = 30, ["reautenticar"] = 5, ["busca"] = 30
        };

        /// <summary>A política "busca" só conta requisições com texto livre (q), que geram embedding pago.</summary>
        private static string UrlQueConta(Rota rota) =>
            rota.RateLimit == "busca" ? rota.Url() + "?q=designer+de+interfaces" : rota.Url();

        private async Task<Sessao?> SessaoParaAsync(Rota rota, string ip) => rota.Guarda switch
        {
            Guarda.Nenhuma => null,
            Guarda.Prestador => await _f.LoginAsync((await _f.NovoParAsync()).Prestador, ip: ip),
            _ => await _f.LoginAsync((await _f.NovoParAsync()).Contratante, ip: ip)
        };

        /// <summary>
        /// A janela do limitador no Redis é alinhada ao minuto cheio (relógio do Redis — o mesmo
        /// da máquina, aqui). Uma sequência que atravesse o ":00" zera o contador no meio e o
        /// teste vira loteria; por isso as contagens só começam com folga dentro do minuto.
        /// </summary>
        private static async Task AguardarInicioDeJanelaAsync()
        {
            var agora = DateTime.UtcNow;
            if (agora.Second < 35) return;
            var proximo = agora.AddSeconds(60 - agora.Second).AddMilliseconds(-agora.Millisecond).AddSeconds(1);
            await Task.Delay(proximo - agora);
        }

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.ComRateLimit), MemberType = typeof(MatrizEndpoints))]
        public async Task CadaRotaLimitada_AceitaOLimite_ERecusaOExcedenteCom429ERetryAfter(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var limite = Limites[rota.RateLimit!];
            var ip = ApiRealFixture.NovoIp();
            var sessao = await SessaoParaAsync(rota, ip);
            await AguardarInicioDeJanelaAsync();

            for (var i = 1; i <= limite; i++)
            {
                using var r = await _f.Api.EnviarAsync(rota.Http, UrlQueConta(rota), sessao, ip: ip);
                Assert.True(r.StatusCode != HttpStatusCode.TooManyRequests, $"{chave}: 429 antes do limite, na requisição {i} de {limite}");
            }

            using var excedente = await _f.Api.EnviarAsync(rota.Http, UrlQueConta(rota), sessao, ip: ip);
            Assert.Equal(HttpStatusCode.TooManyRequests, excedente.StatusCode);
            Assert.Equal("60", excedente.Headers.GetValues("Retry-After").Single());
            var json = await ClienteApi.LerJsonAsync(excedente);
            Assert.Contains("Muitas tentativas", json.GetProperty("mensagem").GetString());

            // Outro cliente não é afetado.
            using var outro = await _f.Api.EnviarAsync(rota.Http, rota.Url(), sessao is null ? null : await SessaoParaAsync(rota, ApiRealFixture.NovoIp()));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, outro.StatusCode);
        }

        [FactApiReal]
        public async Task DuasReplicas_CompartilhamOMesmoContador()
        {
            var ip = ApiRealFixture.NovoIp();
            var corpo = new { email = "ninguem@teste.local", senha = "errada" };
            await AguardarInicioDeJanelaAsync();
            for (var i = 0; i < 5; i++) using (await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo)) { }
            for (var i = 0; i < 5; i++) using (await _f.Replica.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo)) { }

            using var naReplica = await _f.Replica.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo);
            using var naPrincipal = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo);
            Assert.Equal(HttpStatusCode.TooManyRequests, naReplica.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, naPrincipal.StatusCode);
        }

        [FactApiReal]
        [Trait("Lento", "~60s")]
        public async Task AposAJanela_OAcessoVoltaAoNormal()
        {
            var ip = ApiRealFixture.NovoIp();
            var corpo = new { email = "ninguem@teste.local", senha = "errada" };
            for (var i = 0; i < 10; i++) using (await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo)) { }
            using (var bloqueado = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo))
                Assert.Equal(HttpStatusCode.TooManyRequests, bloqueado.StatusCode);

            var relogio = Stopwatch.StartNew();
            HttpStatusCode status;
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                using var r = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip, corpo: corpo);
                status = r.StatusCode;
            } while (status == HttpStatusCode.TooManyRequests && relogio.Elapsed < TimeSpan.FromSeconds(75));

            Assert.NotEqual(HttpStatusCode.TooManyRequests, status);
            Assert.True(relogio.Elapsed <= TimeSpan.FromSeconds(64), $"Janela levou {relogio.Elapsed.TotalSeconds:F0}s para expirar (Retry-After diz 60).");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Contornos
        // ─────────────────────────────────────────────────────────────────────

        public static IEnumerable<object[]> Forjamentos => new[]
        {
            new object[] { "XFF com IPs falsos à esquerda" },
            new object[] { "X-Real-IP" },
            new object[] { "Forwarded (RFC 7239)" },
            new object[] { "CF-Connecting-IP / True-Client-IP / X-Client-IP" },
        };

        [TheoryApiReal]
        [MemberData(nameof(Forjamentos))]
        public async Task HeaderDeIpForjado_NaoEscapaDoLimite(string tecnica)
        {
            var real = ApiRealFixture.NovoIp();
            var corpo = new { email = "ninguem@teste.local", senha = "errada" };
            var respostas = new List<HttpStatusCode>();
            await AguardarInicioDeJanelaAsync();

            for (var i = 0; i < 13; i++)
            {
                var falso = $"203.0.113.{i + 1}";
                using var req = new HttpRequestMessage(HttpMethod.Post, "/usuarios/login") { Content = System.Net.Http.Json.JsonContent.Create(corpo) };
                // A borda da Railway ACRESCENTA ao que o cliente mandou: "<forjado>, <cliente real>, <borda>".
                req.Headers.TryAddWithoutValidation("X-Forwarded-For",
                    tecnica.StartsWith("XFF") ? $"{falso}, {real}, 100.64.0.7" : $"{real}, 100.64.0.7");
                switch (tecnica)
                {
                    case "X-Real-IP": req.Headers.TryAddWithoutValidation("X-Real-IP", falso); break;
                    case "Forwarded (RFC 7239)": req.Headers.TryAddWithoutValidation("Forwarded", $"for={falso};proto=https"); break;
                    case "CF-Connecting-IP / True-Client-IP / X-Client-IP":
                        foreach (var h in new[] { "CF-Connecting-IP", "True-Client-IP", "X-Client-IP", "X-Originating-IP" })
                            req.Headers.TryAddWithoutValidation(h, falso);
                        break;
                }
                using var r = await _f.Api.Http.SendAsync(req);
                respostas.Add(r.StatusCode);
            }

            Assert.All(respostas.Take(10), s => Assert.NotEqual(HttpStatusCode.TooManyRequests, s));
            Assert.All(respostas.Skip(10), s => Assert.Equal(HttpStatusCode.TooManyRequests, s));
        }

        /// <summary>
        /// Conexão direta ao Kestrel por um IP fora das faixas confiáveis (o da interface do
        /// container): o gate descarta o X-Forwarded-For, então trocar o header não muda a partição.
        /// </summary>
        [FactApiReal]
        public async Task PeerForaDaFaixaConfiavel_NaoConsegueEscolherOProprioIp()
        {
            var ipLocal = (await Dns.GetHostAddressesAsync(Dns.GetHostName()))
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)
                                     && !a.ToString().StartsWith("100.", StringComparison.Ordinal));
            if (ipLocal is null) return;   // sem interface não-loopback: nada a provar nesta máquina

            using var http = new HttpClient { BaseAddress = new Uri($"http://{ipLocal}:{_f.Api.BaseUrl.Port}"), Timeout = TimeSpan.FromSeconds(30) };
            var corpo = new { email = "ninguem@teste.local", senha = "errada" };
            var respostas = new List<HttpStatusCode>();
            await AguardarInicioDeJanelaAsync();
            try
            {
                for (var i = 0; i < 12; i++)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, "/usuarios/login") { Content = System.Net.Http.Json.JsonContent.Create(corpo) };
                    req.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{100 + i}, 100.64.0.7");
                    using var r = await http.SendAsync(req);
                    respostas.Add(r.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"Kestrel não respondeu em {ipLocal} (a API escuta em todas as interfaces): {ex.Message}");
            }

            Assert.Contains(HttpStatusCode.TooManyRequests, respostas);
            Assert.True(respostas.IndexOf(HttpStatusCode.TooManyRequests) <= 10, string.Join(",", respostas.Select(s => (int)s)));
        }

        [FactApiReal]
        public async Task VariacoesDeCaminho_CaemNoMesmoBalde()
        {
            var ip = ApiRealFixture.NovoIp();
            var corpo = new { email = "ninguem@teste.local", senha = "errada" };
            var variantes = new[] { "/usuarios/login", "/usuarios/login/", "/USUARIOS/LOGIN", "/Usuarios/Login", "//usuarios/login", "/usuarios/./login", "/usuarios/login?x=1" };
            await AguardarInicioDeJanelaAsync();

            for (var i = 0; i < 10; i++)
                using (await _f.Api.EnviarAsync(HttpMethod.Post, variantes[i % 3], ip: ip, corpo: corpo)) { }

            foreach (var v in variantes)
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Post, v, ip: ip, corpo: corpo);
                Assert.True(r.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    $"{v} escapou do limite depois de esgotado: {(int)r.StatusCode}");
            }
        }

        [FactApiReal]
        public async Task OutroMetodo_NaRotaDeLogin_NaoExecutaOLogin()
        {
            foreach (var m in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                using var r = await _f.Api.EnviarAsync(m, "/usuarios/login", corpo: m == HttpMethod.Get || m == HttpMethod.Delete ? null : new { email = "x", senha = "y" });
                Assert.Equal(HttpStatusCode.MethodNotAllowed, r.StatusCode);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Controles por código (compensam rotas sem rate limit)
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task ConfirmacaoDeCadastro_SemRateLimit_MasCodigoMorreNaQuintaTentativaErrada()
        {
            var ip = ApiRealFixture.NovoIp();
            var email = $"forca.bruta.{Guid.NewGuid():N}"[..28] + "@teste.local";
            var desde = DateTime.UtcNow.AddSeconds(-1);
            using (var cad = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/cadastro", ip: ip, corpo: new
                   { nome = "Força Bruta", email, senha = ApiRealFixture.Senha, confirmarSenha = ApiRealFixture.Senha, mfa = false, tipo = "contratante", aceitouTermos = true }))
                Assert.Equal(HttpStatusCode.OK, cad.StatusCode);
            var codigo = await _f.Externo.CodigoEnviadoParaAsync(email, desde);
            var errado = codigo == "000000" ? "111111" : "000000";

            for (var i = 0; i < 5; i++)
                using (var r = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/cadastro/confirmar", ip: ip, corpo: new { email, codigo = errado }))
                    Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

            using var certo = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/cadastro/confirmar", ip: ip, corpo: new { email, codigo });
            Assert.Equal(HttpStatusCode.BadRequest, certo.StatusCode);   // código já invalidado
        }

        // ─────────────────────────────────────────────────────────────────────
        // Achados
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Achado D4 (corrigido): 40 buscas anônimas = 40 chamadas pagas à OpenAI, sem limite.</summary>
        [FactApiReal]
        public async Task BuscaPublica_ComIA_TemRateLimit()
        {
            var ip = ApiRealFixture.NovoIp();
            await AguardarInicioDeJanelaAsync();
            var antes = _f.Externo.ChamadasEmbeddings;
            var status = new List<HttpStatusCode>();
            for (var i = 0; i < 40; i++)
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/busca/prestadores?q=designer+{Guid.NewGuid():N}", ip: ip);
                status.Add(r.StatusCode);
            }
            var chamadas = _f.Externo.ChamadasEmbeddings - antes;
            Assert.True(status.Contains(HttpStatusCode.TooManyRequests),
                $"40 buscas anônimas do mesmo IP, nenhuma 429; {chamadas} chamadas pagas à OpenAI.");
            Assert.True(chamadas <= 30, $"{chamadas} chamadas à OpenAI a partir de um único IP num minuto (teto: 30).");

            // A isenção "sem q" não pode ser usada para escapar: nome em maiúsculas e parâmetro repetido.
            var antesContorno = _f.Externo.ChamadasEmbeddings;
            foreach (var url in new[] { "/api/busca/prestadores?Q=designer", "/api/busca/prestadores?q=&q=designer", "/api/busca/projetos?Q=react" })
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Get, url, ip: ip);
                Assert.True(r.StatusCode == HttpStatusCode.TooManyRequests, $"{url} escapou do limite: {(int)r.StatusCode}");
            }
            Assert.Equal(antesContorno, _f.Externo.ChamadasEmbeddings);
        }

        /// <summary>
        /// Contraparte do D4: o limite vale só para a busca com texto (a que custa). Listar,
        /// filtrar e paginar sem q continuam livres, mesmo para quem já esgotou a cota.
        /// </summary>
        [FactApiReal]
        public async Task BuscaSemTexto_ContinuaSemLimite_MesmoComACotaDeIaEsgotada()
        {
            var ip = ApiRealFixture.NovoIp();
            await AguardarInicioDeJanelaAsync();
            for (var i = 0; i < 31; i++)
                using (await _f.Api.EnviarAsync(HttpMethod.Get, $"/api/busca/projetos?q=react+{i}", ip: ip)) { }
            using (var esgotada = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/busca/prestadores?q=outra+busca", ip: ip))
                Assert.Equal(HttpStatusCode.TooManyRequests, esgotada.StatusCode);   // cota única por IP nas buscas com texto

            foreach (var url in new[] { "/api/busca/prestadores", "/api/busca/prestadores?categoria=Design&page=2",
                         "/api/busca/projetos", "/api/busca/projetos?q=", "/api/busca/projetos?q=+++", "/api/projects" })
                for (var i = 0; i < 15; i++)
                    using (var r = await _f.Api.EnviarAsync(HttpMethod.Get, url, ip: ip))
                        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url}: {(int)r.StatusCode}");

            using var outroIp = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/busca/prestadores?q=designer");
            Assert.True(outroIp.StatusCode == HttpStatusCode.OK, $"Outro IP: {(int)outroIp.StatusCode} {await outroIp.Content.ReadAsStringAsync()}");
        }

        public static IEnumerable<object[]> RotasSensiveisSemLimite => new[]
        {
            new object[] { "POST /usuarios/cadastro/confirmar" },
            new object[] { "POST /api/contratos/{id:guid}/assinar" },
            new object[] { "POST /api/projects/{id:int}/mensagens" },
            new object[] { "POST /api/pagamentos/iniciar" },
            new object[] { "POST /prestadores/me/foto" },
            new object[] { "POST /api/contratos/{id:guid}/entrega" },
            new object[] { "GET /api/cep/{cep}" },
            new object[] { "GET /api/admin/disputas" },
        };

        [TheoryAchado("D5")]
        [MemberData(nameof(RotasSensiveisSemLimite))]
        public async Task RotaSensivel_TemRateLimit(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var ip = ApiRealFixture.NovoIp();
            var sessao = rota.Guarda switch
            {
                Guarda.Nenhuma => null,
                Guarda.Admin => await _f.SessaoAsync(_f.Dados.Admin),
                Guarda.Prestador => await _f.SessaoAsync(_f.Dados.PrestadorVarredura),
                _ => await _f.SessaoAsync(_f.Dados.ContratanteVarredura)
            };
            var url = chave.Contains("/api/cep/") ? "/api/cep/123" : rota.Url();   // CEP inválido: não chama o ViaCEP

            var viu429 = false;
            for (var i = 0; i < 40 && !viu429; i++)
            {
                using var r = await _f.Api.EnviarAsync(rota.Http, url, sessao, ip: ip);
                viu429 = r.StatusCode == HttpStatusCode.TooManyRequests;
            }
            Assert.True(viu429, $"{chave}: 40 requisições em sequência do mesmo cliente, nenhuma 429.");
        }

        [FactAchado("D7")]
        public async Task LoginDistribuido_ContraUmaConta_EContido()
        {
            var (vitima, _, _) = await _f.NovoParAsync();
            for (var i = 0; i < 30; i++)
                using (await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ApiRealFixture.NovoIp(),
                           corpo: new { email = vitima.Email, senha = $"Tentativa{i}!" })) { }

            // Depois de 30 senhas erradas (de 30 IPs), a conta deveria estar protegida.
            using var certo = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ApiRealFixture.NovoIp(),
                corpo: new { email = vitima.Email, senha = ApiRealFixture.Senha });
            Assert.True(certo.StatusCode != HttpStatusCode.OK,
                "30 senhas erradas de 30 IPs diferentes e a conta segue aceitando login sem nenhuma contenção.");
        }
    }
}
