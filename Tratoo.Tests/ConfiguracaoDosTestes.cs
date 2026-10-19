using System.Runtime.CompilerServices;
using Tratoo.Domain.Features.Shared;

namespace Tratoo.Tests
{
    /// <summary>
    /// Configuração que vale para a suíte inteira, aplicada quando o assembly de testes é
    /// carregado (antes de qualquer teste).
    ///
    /// A chave de criptografia de dados saiu do código-fonte: em produção vem de
    /// <c>DataProtector__Chave</c>. Os testes usam uma chave PRÓPRIA, fixa e sem valor fora
    /// daqui — assim a suíte não depende de variável de ambiente da máquina nem jamais usa
    /// (ou precisa conhecer) a chave de produção.
    /// </summary>
    internal static class ConfiguracaoDosTestes
    {
        public const string ChaveDeDadosDeTeste = "chave-exclusiva-da-suite-de-testes-0123456789";

#pragma warning disable CA2255 // ModuleInitializer num assembly de testes é exatamente o uso pretendido aqui
        [ModuleInitializer]
        internal static void Inicializar() => DataProtector.Configurar(ChaveDeDadosDeTeste);
#pragma warning restore CA2255
    }
}
