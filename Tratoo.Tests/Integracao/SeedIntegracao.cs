using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Dados mínimos e VÁLIDOS para os fluxos reais (mesma forma do seed de dev em
    /// DevSeedExtensions): contratante, prestador apto com chave PIX cifrada, projeto,
    /// proposta, contrato e, opcionalmente, um pagamento retido com liberação vencida.
    /// </summary>
    internal static class SeedIntegracao
    {
        public sealed record Cenario(
            int ContratanteId, int PrestadorId, int ProjetoId, Guid PropostaId, Guid ContratoId, Guid? PagamentoId);

        public static TratooContext Contexto(string conexao) =>
            new(new DbContextOptionsBuilder<TratooContext>().UseNpgsql(conexao).Options);

        public static async Task<Cenario> CriarAsync(
            string conexao,
            ContratoServicoStatus statusContrato = ContratoServicoStatus.Ativo,
            DateTime? contratoExpiraEm = null,
            bool comPagamentoRetidoVencido = false)
        {
            await using var db = Contexto(conexao);
            var agora = DateTime.UtcNow;
            var sufixo = Guid.NewGuid().ToString("N")[..8];

            var contratante = new Contratante
            {
                Nome = "Contratante Integração",
                Email = $"contratante.{sufixo}@teste.local",
                SenhaHash = "nao-usado",
                TipoUsuario = TipoUsuario.Contratante,
                Status = StatusUsuario.Active,
                IdentidadeVerificada = true,
                TipoPessoa = TipoPessoa.PessoaFisica,
                Endereco = new Endereco
                {
                    Cep = "01310-100", Logradouro = "Av. Paulista", Numero = "1000",
                    Bairro = "Bela Vista", Cidade = "São Paulo", Estado = "SP"
                }
            };
            contratante.VerificarPerfilMinimo();

            var prestador = new Prestador
            {
                Nome = "Prestador Integração",
                Email = $"prestador.{sufixo}@teste.local",
                SenhaHash = "nao-usado",
                TipoUsuario = TipoUsuario.Prestador,
                Status = StatusUsuario.Active,
                IdentidadeVerificada = true,
                TipoPessoa = TipoPessoa.PessoaFisica,
                AreaEspecializacao = "Design",
                FuncaoExecutada = "Designer",
                Disponivel = true,
                Endereco = new Endereco
                {
                    Cep = "04538-133", Logradouro = "Av. Brigadeiro Faria Lima", Numero = "3900",
                    Bairro = "Itaim Bibi", Cidade = "São Paulo", Estado = "SP"
                }
            };
            prestador.VerificarPerfilMinimo();

            db.Contratantes.Add(contratante);
            db.Prestadores.Add(prestador);
            await db.SaveChangesAsync();

            // Chave PIX aleatória (EVP) válida, cifrada como o DadosBancariosService grava.
            db.ContasBancarias.Add(new ContaBancaria
            {
                PrestadorId = prestador.Id,
                Banco = "001",
                Agencia = "1234",
                ContaCriptografada = DataProtector.Encrypt("123456"),
                TipoPix = TipoPix.Aleatoria,
                PixChave = DataProtector.Encrypt(Guid.NewGuid().ToString()),
                Ativa = true,
                CriadoEm = agora
            });

            var projeto = new Projeto
            {
                ContratanteId = contratante.Id,
                Titulo = $"Projeto integração {sufixo}",
                Descricao = "Projeto criado pelos testes de integração.",
                Categoria = CategoriaProjet.Design,
                OrcamentoMin = 1200m,
                OrcamentoMax = 1200m,
                PrazoEntrega = agora.AddDays(30),
                Status = StatusProjeto.EmAndamento,
                Visibilidade = VisibilidadeProjeto.Publico,
                Publicado = true,
                PublicadoEm = agora.AddDays(-10),
                FreelancerSelecionadoId = prestador.Id
            };
            db.Projetos.Add(projeto);
            await db.SaveChangesAsync();

            var proposta = new PropostaProjeto
            {
                ProjetoId = projeto.Id,
                PrestadorId = prestador.Id,
                Status = StatusPropostaProjeto.Convertida,
                VersaoAtual = 1,
                ValidoAte = agora.AddDays(30)
            };
            db.PropostasProjeto.Add(proposta);
            await db.SaveChangesAsync();

            var ativo = statusContrato == ContratoServicoStatus.Ativo;
            var contrato = new ContratoServico
            {
                ProjetoId = projeto.Id,
                PropostaId = proposta.Id,
                ContratanteId = contratante.Id,
                PrestadorId = prestador.Id,
                Status = statusContrato,
                ConteudoJson = "{\"titulo\":\"integração\"}",
                ConteudoHash = "HASH_" + sufixo,
                AssinadoContratanteEm = ativo ? agora.AddDays(-5) : null,
                AssinadoPrestadorEm = ativo ? agora.AddDays(-5) : null,
                IpContratante = "127.0.0.1",
                IpPrestador = "127.0.0.1",
                CriadoEm = agora.AddDays(-8),
                ExpiraEm = contratoExpiraEm ?? agora.AddDays(90)
            };
            db.ContratosServico.Add(contrato);
            await db.SaveChangesAsync();

            Guid? pagamentoId = null;
            if (comPagamentoRetidoVencido)
            {
                var pagamento = new Pagamento
                {
                    ContratoServicoId = contrato.Id,
                    ValorBruto = 1200m,
                    Status = StatusPagamento.Retido,
                    Metodo = MetodoPagamento.Pix,
                    Gateway = "Asaas",
                    GatewayPagamentoId = "pay_integracao_" + sufixo,
                    AsaasClienteId = "cus_integracao",
                    StatusGateway = "RECEIVED",
                    PagoEm = agora.AddDays(-10),
                    LiberacaoAutomaticaEm = agora.AddDays(-1)   // prazo vencido
                };
                db.Pagamentos.Add(pagamento);
                await db.SaveChangesAsync();
                pagamentoId = pagamento.Id;
            }

            return new Cenario(contratante.Id, prestador.Id, projeto.Id, proposta.Id, contrato.Id, pagamentoId);
        }
    }
}
