using Tratoo.Domain.Features.Shared;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// A chave de criptografia de dados saiu do código-fonte e passou a ser obrigatória na
    /// configuração. Estes testes rodam o PROCESSO real da API para travar o comportamento de
    /// subida: sem a chave, ela não sobe — um deploy assim falha no health check e a versão
    /// anterior continua no ar, em vez de subir "saudável" e quebrar na primeira leitura de
    /// conta bancária, PIX ou CPF/CNPJ.
    /// </summary>
    public class ChaveDeDadosNaSubidaTests
    {
        private static Dictionary<string, string?> Ambiente(string? chaveDeDados, string? chaveAnterior = null)
        {
            // Banco propositalmente inalcançável: o que se observa aqui acontece ANTES dele.
            var conexao = $"Host=127.0.0.1;Port={ProcessoApi.PortaLivre()};Database=x;Username=x;Password=x;Timeout=3";
            var ambiente = new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["PORT"] = ProcessoApi.PortaLivre().ToString(),
                ["ConnectionStrings__DefaultConnection"] = conexao,
                ["ConnectionStrings__VectorConnection"] = conexao
            };
            if (chaveDeDados is not null)
                ambiente[DataProtector.VariavelDeAmbiente] = chaveDeDados;
            if (chaveAnterior is not null)
                ambiente[DataProtector.VariavelDeAmbienteAnterior] = chaveAnterior;
            return ambiente;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("DEFINA_UMA_CHAVE_ALEATORIA_COM_32_OU_MAIS_CARACTERES")]
        public async Task Sem_chave_valida_a_API_nao_sobe_e_diz_o_que_falta(string? chave)
        {
            var (exit, saida) = await ProcessoApi.RodarAsync(argumento: null, Ambiente(chave));

            Assert.Equal(1, exit);                                  // saída limpa, não exceção não tratada
            Assert.Contains("[startup]", saida);
            Assert.Contains(DataProtector.VariavelDeAmbiente, saida);
            Assert.DoesNotContain("Unhandled exception", saida);
            Assert.DoesNotContain("Now listening", saida);
            Assert.DoesNotContain("Service iniciado", saida);
        }

        [Fact]
        public async Task Com_a_chave_a_subida_passa_da_verificacao_e_segue_para_o_banco()
        {
            var (_, saida) = await ProcessoApi.RodarAsync(argumento: null, Ambiente("uma-chave-de-teste-qualquer-0123456789"));

            // O banco é inalcançável, então o processo cai mais adiante — mas NÃO por causa da
            // chave: a mensagem de chave ausente/placeholder não pode aparecer.
            Assert.DoesNotContain("Chave de criptografia de dados sensíveis ausente", saida);
            Assert.DoesNotContain("placeholder", saida);
        }

        [Fact]
        public async Task Modo_migracao_nao_exige_a_chave_de_dados()
        {
            // O --migrate-only não lê nem grava dado cifrado; exigir a chave ali só criaria
            // mais um segredo para o pre-deploy.
            var (exit, saida) = await ProcessoApi.RodarAsync("--migrate-only", Ambiente(chaveDeDados: null));

            Assert.DoesNotContain(DataProtector.VariavelDeAmbiente, saida);
            Assert.Contains("Migração falhou", saida); // falha pelo banco inalcançável, como esperado
            Assert.NotEqual(0, exit);
        }

        [Fact]
        public async Task Chave_anterior_igual_a_atual_impede_a_subida()
        {
            const string chave = "uma-chave-de-teste-qualquer-0123456789";
            var (exit, saida) = await ProcessoApi.RodarAsync(argumento: null, Ambiente(chave, chaveAnterior: chave));

            Assert.Equal(1, exit);
            Assert.Contains("[startup]", saida);
            Assert.Contains(DataProtector.VariavelDeAmbienteAnterior, saida);
            Assert.DoesNotContain("Now listening", saida);
        }

        [Fact]
        public async Task Recifragem_sem_chave_anterior_recusa_e_explica()
        {
            var (exit, saida) = await ProcessoApi.RodarAsync("--recifrar-dados", Ambiente("chave-nova-de-teste-0123456789-abcdef"));

            Assert.Equal(2, exit);
            Assert.Contains(DataProtector.VariavelDeAmbienteAnterior, saida);
            Assert.DoesNotContain("Now listening", saida);
        }

        [Fact]
        public async Task Recifragem_com_as_duas_chaves_segue_para_o_banco()
        {
            var (exit, saida) = await ProcessoApi.RodarAsync(
                "--recifrar-dados",
                Ambiente("chave-nova-de-teste-0123456789-abcdef", chaveAnterior: "chave-antiga-de-teste-0123456789-abcd"));

            // Banco inalcançável: falha DEPOIS de aceitar a configuração, sem servir nada.
            Assert.Equal(1, exit);
            Assert.Contains("Recifragem falhou", saida);
            Assert.DoesNotContain("Recifragem abortada", saida);
            Assert.DoesNotContain("Now listening", saida);
        }
    }
}
