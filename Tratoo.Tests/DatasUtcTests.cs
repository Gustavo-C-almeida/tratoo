using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tratoo.API.Requests;
using Tratoo.Domain.Data;
using Tratoo.Domain.Features.Shared;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Semântica das datas que chegam de fora do sistema. As colunas são
    /// <c>timestamp with time zone</c>, e o Npgsql só grava nelas <see cref="DateTimeKind.Utc"/>.
    /// A gravação real é coberta por <c>Integracao/DatasPersistenciaTests</c>.
    /// </summary>
    public class DatasUtcTests
    {
        // ── Origem do problema ──────────────────────────────────────────────────

        [Theory]
        [InlineData("2026-12-31", DateTimeKind.Unspecified)]
        [InlineData("2026-12-31T10:00:00", DateTimeKind.Unspecified)]
        [InlineData("2026-12-31T10:00:00-03:00", DateTimeKind.Local)]
        [InlineData("2026-12-31T10:00:00Z", DateTimeKind.Utc)]
        public void Json_DefineOKindPeloTextoRecebido(string texto, DateTimeKind esperado)
        {
            var request = JsonSerializer.Deserialize<CriarProjetoRequest>(
                $$"""{"PrazoEntrega":"{{texto}}"}""");

            Assert.Equal(esperado, request!.PrazoEntrega.Kind);
        }

        // ── Instante ────────────────────────────────────────────────────────────

        [Fact]
        public void Instante_ValorUtc_FicaIgual()
        {
            var valor = new DateTime(2026, 12, 31, 10, 30, 0, DateTimeKind.Utc);

            Assert.Equal(valor, DatasUtc.Instante(valor));
        }

        [Fact]
        public void Instante_SemFuso_ManteOHorarioEMarcaComoUtc()
        {
            var resultado = DatasUtc.Instante(new DateTime(2026, 12, 31, 10, 30, 0, DateTimeKind.Unspecified));

            Assert.Equal(DateTimeKind.Utc, resultado.Kind);
            Assert.Equal(new DateTime(2026, 12, 31, 10, 30, 0), resultado);
        }

        [Fact]
        public void Instante_ValorLocal_ConverteSemMudarOInstante()
        {
            var local = new DateTime(2026, 12, 31, 10, 30, 0, DateTimeKind.Local);

            var resultado = DatasUtc.Instante(local);

            Assert.Equal(DateTimeKind.Utc, resultado.Kind);
            Assert.Equal(local.ToUniversalTime(), resultado);
        }

        [Fact]
        public void Instante_Nulo_ContinuaNulo() => Assert.Null(DatasUtc.Instante((DateTime?)null));

        // ── Data de calendário ──────────────────────────────────────────────────

        [Theory]
        [InlineData(DateTimeKind.Unspecified)]
        [InlineData(DateTimeKind.Utc)]
        public void Calendario_PreservaODiaEZeraOHorario(DateTimeKind kind)
        {
            var resultado = DatasUtc.Calendario(new DateTime(2026, 12, 31, 23, 59, 0, kind));

            Assert.Equal(DateTimeKind.Utc, resultado.Kind);
            Assert.Equal(new DateTime(2026, 12, 31), resultado);
        }

        [Fact]
        public void Calendario_Nulo_ContinuaNulo() => Assert.Null(DatasUtc.Calendario((DateTime?)null));

        // ── Virada do dia no Brasil ─────────────────────────────────────────────

        [Theory]
        [InlineData("2026-11-04T01:00:00Z", "2026-11-03")] // 22h de 03/11 em Brasília
        [InlineData("2026-11-04T02:59:59Z", "2026-11-03")]
        [InlineData("2026-11-04T03:00:00Z", "2026-11-04")] // meia-noite em Brasília
        public void HojeNoBrasil_ViraODiaNaMeiaNoiteDeBrasilia(string agora, string esperado)
        {
            var resultado = DatasUtc.HojeNoBrasil(Utc(agora));

            Assert.Equal(DateTimeKind.Utc, resultado.Kind);
            Assert.Equal(Utc(esperado), resultado);
        }

        [Theory]
        [InlineData("2026-11-04", true)]  // amanhã, embora já seja 04/11 em UTC
        [InlineData("2026-11-03", false)] // hoje
        [InlineData("2026-11-02", false)] // ontem
        public void EhFutura_As22hDeBrasilia_ComparaOsDiasDoBrasil(string data, bool esperado)
        {
            var agora = Utc("2026-11-04T01:00:00Z");

            Assert.Equal(esperado, DatasUtc.EhFutura(Utc(data), agora));
        }

        [Fact]
        public void EhFutura_DataNaoInformada_EhFalso() => Assert.False(DatasUtc.EhFutura(default));

        // ── Leitura de formulário ───────────────────────────────────────────────

        [Theory]
        [InlineData("pt-BR")]
        [InlineData("en-US")]
        public void TentarLer_NaoDependeDaCultura(string cultura)
        {
            var anterior = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(cultura);
            try
            {
                Assert.True(DatasUtc.TentarLer("2026-03-04", out var valor));
                Assert.Equal(DateTimeKind.Utc, valor.Kind);
                Assert.Equal(new DateTime(2026, 3, 4), valor);
            }
            finally
            {
                CultureInfo.CurrentCulture = anterior;
            }
        }

        [Fact]
        public void TentarLer_ComOffset_ConverteParaUtc()
        {
            Assert.True(DatasUtc.TentarLer("2026-12-31T10:00:00-03:00", out var valor));

            Assert.Equal(DateTimeKind.Utc, valor.Kind);
            Assert.Equal(new DateTime(2026, 12, 31, 13, 0, 0), valor);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("não é data")]
        public void TentarLer_TextoInvalido_RetornaFalso(string? texto) =>
            Assert.False(DatasUtc.TentarLer(texto, out _));

        // ── Auditoria do modelo ─────────────────────────────────────────────────

        [Fact]
        public void Modelo_TodoDateTimeEhTimestamptzETodoDateOnlyEhDate()
        {
            var erros = PropriedadesDoModelo()
                .Where(p => (p.Tipo == typeof(DateTime) && p.Coluna != "timestamp with time zone")
                         || (p.Tipo == typeof(DateOnly) && p.Coluna != "date"))
                .Select(p => $"{p.Entidade}.{p.Nome}: {p.Coluna}")
                .ToList();

            Assert.True(erros.Count == 0, string.Join(Environment.NewLine, erros));
        }

        [Fact]
        public void Modelo_ValorPadraoDeDataSempreNasceEmUtc()
        {
            using var db = ContextoSemConexao();

            var erros = new List<string>();
            foreach (var entidade in db.Model.GetEntityTypes().Where(e => !e.ClrType.IsAbstract && !e.HasSharedClrType))
            {
                if (entidade.ClrType.GetConstructor(Type.EmptyTypes) is null)
                    continue;

                var instancia = Activator.CreateInstance(entidade.ClrType)!;
                foreach (var propriedade in entidade.GetProperties().Where(p => p.PropertyInfo is not null))
                {
                    var valor = propriedade.PropertyInfo!.GetValue(instancia);
                    if (valor is DateTime data && data != default && data.Kind != DateTimeKind.Utc)
                        erros.Add($"{entidade.ClrType.Name}.{propriedade.Name}: {data.Kind}");
                }
            }

            Assert.True(erros.Count == 0, string.Join(Environment.NewLine, erros));
        }

        private static IEnumerable<(string Entidade, string Nome, Type Tipo, string? Coluna)> PropriedadesDoModelo()
        {
            using var db = ContextoSemConexao();

            return db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetEntityTypes()
                .SelectMany(e => e.GetProperties().Select(p => (
                    e.ClrType.Name,
                    p.Name,
                    Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType,
                    p.GetColumnType())))
                .ToList();
        }

        // A conexão nunca é aberta: os testes só leem os metadados do modelo.
        private static TratooContext ContextoSemConexao() =>
            new(new DbContextOptionsBuilder<TratooContext>()
                .UseNpgsql("Host=127.0.0.1;Database=sem_conexao")
                .Options);

        private static DateTime Utc(string texto) =>
            DateTime.Parse(texto, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
