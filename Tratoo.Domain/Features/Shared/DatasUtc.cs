using System.Globalization;

namespace Tratoo.Domain.Features.Shared
{
    /// <summary>
    /// Normaliza as datas que chegam de fora do sistema antes de irem para colunas
    /// <c>timestamp with time zone</c>, que no Npgsql só aceitam <see cref="DateTimeKind.Utc"/>.
    ///
    /// A origem do problema: um <c>&lt;input type="date"&gt;</c> envia <c>"2026-12-31"</c>, sem
    /// fuso, e o .NET converte isso em <see cref="DateTimeKind.Unspecified"/>. Quem desserializa
    /// um offset como <c>-03:00</c> recebe <see cref="DateTimeKind.Local"/>, recusado do mesmo jeito.
    ///
    /// O sistema tem dois tipos de data, e cada um usa um método:
    /// <list type="bullet">
    /// <item><see cref="Instante(DateTime)"/>: um ponto no tempo, em que o horário importa
    /// (validade de proposta, expiração do PIX, filtros sobre carimbos de data).</item>
    /// <item><see cref="Calendario(DateTime)"/>: um dia, sem horário (prazo de entrega,
    /// início de experiência, emissão de certificado). Fica guardado à meia-noite UTC, e por
    /// isso o front exibe esses campos em UTC (ver <c>utils/datas.js</c>).</item>
    /// </list>
    /// Carimbos gerados pelo servidor não passam por aqui: já nascem de <see cref="DateTime.UtcNow"/>.
    /// </summary>
    public static class DatasUtc
    {
        // Horário de Brasília. O Brasil não adota horário de verão desde 2019, e um deslocamento
        // fixo não depende do pacote tzdata na imagem do contêiner.
        private static readonly TimeSpan FusoBrasilia = TimeSpan.FromHours(-3);

        /// <summary>Converte para UTC preservando o instante. Sem fuso, o valor é lido como UTC.</summary>
        public static DateTime Instante(DateTime valor) => valor.Kind switch
        {
            DateTimeKind.Utc => valor,
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
        };

        /// <inheritdoc cref="Instante(DateTime)"/>
        public static DateTime? Instante(DateTime? valor) => valor.HasValue ? Instante(valor.Value) : null;

        /// <summary>Reduz o valor ao dia informado, à meia-noite UTC.</summary>
        public static DateTime Calendario(DateTime valor) => Instante(valor).Date;

        /// <inheritdoc cref="Calendario(DateTime)"/>
        public static DateTime? Calendario(DateTime? valor) => valor.HasValue ? Calendario(valor.Value) : null;

        /// <summary>O dia atual em Brasília, à meia-noite UTC.</summary>
        public static DateTime HojeNoBrasil(DateTime? agoraUtc = null) =>
            DateTime.SpecifyKind(((agoraUtc ?? DateTime.UtcNow) + FusoBrasilia).Date, DateTimeKind.Utc);

        /// <summary>
        /// Indica se o dia ainda não chegou em Brasília. Comparar com <see cref="DateTime.UtcNow"/>
        /// recusaria o dia seguinte a partir das 21h, quando já passou da meia-noite UTC.
        /// </summary>
        public static bool EhFutura(DateTime dataCalendario, DateTime? agoraUtc = null) =>
            Calendario(dataCalendario) > HojeNoBrasil(agoraUtc);

        /// <summary>
        /// Lê uma data ISO 8601 de formulário ou query string, sem depender da cultura nem do fuso
        /// do servidor. Texto sem fuso é lido como UTC.
        /// </summary>
        public static bool TentarLer(string? texto, out DateTime valor) =>
            DateTime.TryParse(
                texto,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out valor);
    }
}
