using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tratoo.Domain.Data;
using Tratoo.Domain.Features.Shared;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Readiness do banco relacional principal (Neon PostgreSQL, base "tratoo").
    ///
    /// Implementado à mão em vez de <c>AddDbContextCheck</c> de propósito: aquele
    /// método vive no pacote Microsoft.Extensions.Diagnostics.HealthChecks
    /// .EntityFrameworkCore, que NÃO faz parte do shared framework e seria uma
    /// dependência nova — e ele faz exatamente o que está aqui
    /// (<c>CanConnectAsync</c>). O projeto já teve conflito de versões da família EF
    /// Core (MSB3277, resolvido declarando o Relational 9.0.10 no Tratoo.Domain), então
    /// evitar mais um pacote dessa família tem valor concreto.
    /// </summary>
    public sealed class PostgresHealthCheck : IHealthCheck
    {
        private readonly TratooContext _db;

        public PostgresHealthCheck(TratooContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _db.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("PostgreSQL acessível.")
                    : HealthCheckResult.Unhealthy("PostgreSQL não respondeu à verificação de conexão.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Falha ao conectar no PostgreSQL.", ex);
            }
        }
    }

    /// <summary>
    /// Readiness da base vetorial (Neon PostgreSQL, base "tratoo_vector").
    ///
    /// Vai além de "a conexão abre": confirma que a extensão <c>vector</c> está
    /// instalada. As duas bases ficam no mesmo host Neon, então um simples
    /// CanConnect na base vetorial diria pouco além do que o check de PostgreSQL
    /// já diz — e a busca semântica depende da extensão, não só da base.
    /// </summary>
    public sealed class PgVectorHealthCheck : IHealthCheck
    {
        private readonly VectorContext _vectorDb;

        public PgVectorHealthCheck(VectorContext vectorDb) => _vectorDb = vectorDb;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Uma única ida ao banco resolve as duas perguntas: se a query executa,
                // a conexão está de pé; se devolve linha, a extensão existe.
                // O alias "Value" é exigido pelo SqlQueryRaw para tipos escalares.
                var linhas = await _vectorDb.Database
                    .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
                    .ToListAsync(cancellationToken);

                return linhas.Count > 0
                    ? HealthCheckResult.Healthy("Base vetorial acessível e extensão pgvector instalada.")
                    : HealthCheckResult.Unhealthy(
                        "Base vetorial acessível, mas a extensão pgvector não está instalada.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Falha ao verificar a base vetorial/pgvector.", ex);
            }
        }
    }

    /// <summary>
    /// Lembra que a chave já foi conferida contra dados reais neste processo. A chave não
    /// muda sem reiniciar, então depois do primeiro "ok" o check não consulta mais o banco.
    /// </summary>
    public sealed class EstadoChaveDados
    {
        private volatile bool _confirmada;
        public bool Confirmada { get => _confirmada; set => _confirmada = value; }
    }

    /// <summary>
    /// Readiness: a chave configurada (<c>DataProtector:Chave</c>) decifra os dados que JÁ
    /// existem no banco?
    ///
    /// A API subir e o health check de banco passar não dizem nada sobre a chave: com uma
    /// chave diferente da usada para cifrar, tudo parece saudável até alguém abrir os dados
    /// bancários ou uma liberação de pagamento falhar. Este check pega algumas amostras
    /// reais de cada coluna cifrada (chave PIX, conta, CPF/CNPJ, CPF do representante) e
    /// tenta decifrá-las:
    ///
    ///  • nenhuma amostra decifra → Unhealthy (503). No deploy da Railway isso impede a
    ///    promoção: a versão com a chave errada nunca recebe tráfego;
    ///  • parte decifra → Degraded (200, visível no corpo) — dados cifrados com mais de uma
    ///    chave, o que só deve acontecer durante uma rotação;
    ///  • rotação em curso (DataProtector:ChaveAnterior configurada) e há amostras ainda na
    ///    chave anterior → Degraded, até o <c>--recifrar-dados</c> regravá-las;
    ///  • todas decifram com a chave atual, ou não há dado cifrado → Healthy.
    ///
    /// Só entram valores com FORMA de dado cifrado (<see cref="DataProtector.PareceCifrado"/>):
    /// valores fictícios de seed não contam. Nada decifrado sai daqui — só contagens.
    /// </summary>
    public sealed class DadosCifradosHealthCheck : IHealthCheck
    {
        private const int AmostrasPorColuna = 3;

        private readonly TratooContext _db;
        private readonly EstadoChaveDados _estado;
        private readonly ChavesDeDados? _chaves;

        /// <param name="chaves">Só para testes; na API valem as chaves configuradas na subida.</param>
        public DadosCifradosHealthCheck(TratooContext db, EstadoChaveDados estado, ChavesDeDados? chaves = null)
        {
            _db = db;
            _estado = estado;
            _chaves = chaves;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_estado.Confirmada)
                return HealthCheckResult.Healthy("Chave de dados já conferida contra dados existentes.");

            try
            {
                var amostras = new List<string?>();

                amostras.AddRange(await _db.ContasBancarias.AsNoTracking()
                    .OrderByDescending(c => c.Id).Select(c => c.PixChave)
                    .Take(AmostrasPorColuna).ToListAsync(cancellationToken));
                amostras.AddRange(await _db.ContasBancarias.AsNoTracking()
                    .OrderByDescending(c => c.Id).Select(c => c.ContaCriptografada)
                    .Take(AmostrasPorColuna).ToListAsync(cancellationToken));
                amostras.AddRange(await _db.UserIdentities.AsNoTracking()
                    .OrderByDescending(u => u.Id).Select(u => u.CpfCnpjCriptografado)
                    .Take(AmostrasPorColuna).ToListAsync(cancellationToken));
                amostras.AddRange(await _db.UserIdentities.AsNoTracking()
                    .Where(u => u.ChavePixCriptografada != null)
                    .OrderByDescending(u => u.Id).Select(u => u.ChavePixCriptografada)
                    .Take(AmostrasPorColuna).ToListAsync(cancellationToken));
                amostras.AddRange(await _db.UserIdentities.AsNoTracking()
                    .Where(u => u.CpfRepresentanteLegalCriptografado != null)
                    .OrderByDescending(u => u.Id).Select(u => u.CpfRepresentanteLegalCriptografado)
                    .Take(AmostrasPorColuna).ToListAsync(cancellationToken));

                // Nada decifrado sai daqui: só a classificação de cada amostra.
                var chaves = _chaves ?? DataProtector.Chaves;
                var origens = amostras
                    .Select(a => DataProtector.Identificar(a, chaves))
                    .Where(o => o != ChaveDoValor.NaoCifrado)
                    .ToList();
                if (origens.Count == 0)
                    return HealthCheckResult.Healthy("Nenhum dado cifrado para conferir.");

                var naAnterior = origens.Count(o => o == ChaveDoValor.Anterior);
                var decifradas = origens.Count(o => o != ChaveDoValor.Nenhuma);

                if (decifradas == origens.Count && naAnterior > 0)
                    return HealthCheckResult.Degraded(
                        $"Rotação de chave em curso: {naAnterior} de {origens.Count} amostras ainda na chave anterior. " +
                        "Rode --recifrar-dados (Docs/ROTACAO-CHAVE-DADOS.md).");

                if (decifradas == origens.Count)
                {
                    _estado.Confirmada = true;
                    return HealthCheckResult.Healthy(
                        $"Chave de dados confere: {decifradas} de {origens.Count} amostras decifradas.");
                }

                if (decifradas == 0)
                    return HealthCheckResult.Unhealthy(
                        $"A chave configurada NÃO decifra os dados existentes (0 de {origens.Count} amostras). " +
                        "DataProtector__Chave não é a chave com que os dados foram cifrados.");

                return HealthCheckResult.Degraded(
                    $"Chave de dados decifra só {decifradas} de {origens.Count} amostras (dados cifrados com mais de uma chave).");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Falha ao conferir a chave de dados contra o banco.", ex);
            }
        }
    }
}
