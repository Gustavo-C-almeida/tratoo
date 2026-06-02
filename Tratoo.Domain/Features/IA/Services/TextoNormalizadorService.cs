using System.Text;
using System.Text.Json;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Features.IA
{
    /// <summary>
    /// Constrói o texto normalizado que alimenta o embedding de prestadores e projetos.
    /// Usa seções rotuladas para que o modelo multilingual-e5-base entenda o contexto de
    /// cada bloco — melhora significativamente a qualidade do embedding.
    /// </summary>
    public class TextoNormalizadorService
    {
        public string NormalizarPrestador(Prestador prestador, IEnumerable<string>? comentariosPublicos = null)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(prestador.TituloProfissional))
                sb.Append($"Profissional: {prestador.TituloProfissional}. ");

            var especializacao = new[] { prestador.AreaEspecializacao, prestador.FuncaoExecutada }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            if (especializacao.Any())
                sb.Append($"Especialização: {string.Join(", ", especializacao)}. ");

            if (!string.IsNullOrWhiteSpace(prestador.Descricao))
                sb.Append($"Sobre: {prestador.Descricao.Trim()} ");

            if (prestador.Competencias?.Any() == true)
            {
                var nomes = prestador.Competencias.Select(c => c.Nome);
                sb.Append($"Competências: {string.Join(", ", nomes)}. ");
            }

            if (prestador.Experiencias?.Any() == true)
            {
                foreach (var exp in prestador.Experiencias)
                {
                    sb.Append($"Experiência: {exp.Cargo} em {exp.Empresa}.");
                    if (!string.IsNullOrWhiteSpace(exp.Atividades))
                        sb.Append($" {exp.Atividades.Trim()}");
                    sb.Append(' ');
                }
            }

            if (prestador.Certificacoes?.Any() == true)
            {
                foreach (var cert in prestador.Certificacoes)
                    sb.Append($"Certificação: {cert.Nome} por {cert.InstituicaoEmissora}. ");
            }

            if (prestador.Portfolio?.Any() == true)
            {
                foreach (var item in prestador.Portfolio)
                {
                    if (string.IsNullOrWhiteSpace(item.Titulo)) continue;
                    sb.Append($"Portfólio: {item.Titulo}");
                    if (!string.IsNullOrWhiteSpace(item.Descricao))
                        sb.Append($" — {item.Descricao.Trim()}");
                    sb.Append(". ");
                }
            }

            if (comentariosPublicos != null)
            {
                foreach (var c in comentariosPublicos.Where(c => !string.IsNullOrWhiteSpace(c)))
                    sb.Append($"Feedback: {c.Trim()} ");
            }

            return sb.ToString().Trim();
        }

        public string NormalizarProjeto(Projeto projeto)
        {
            var sb = new StringBuilder();

            sb.Append($"Projeto: {projeto.Titulo}. ");
            sb.Append($"Categoria: {projeto.Categoria}. ");

            if (!string.IsNullOrWhiteSpace(projeto.Descricao))
                sb.Append($"Descrição: {projeto.Descricao.Trim()} ");

            if (!string.IsNullOrWhiteSpace(projeto.Habilidades))
            {
                try
                {
                    var habs = JsonSerializer.Deserialize<List<string>>(projeto.Habilidades);
                    if (habs?.Any() == true)
                        sb.Append($"Tecnologias desejadas: {string.Join(", ", habs)}. ");
                }
                catch { /* JSON inválido — ignora */ }
            }

            sb.Append($"Orçamento: R${projeto.OrcamentoMin} a R${projeto.OrcamentoMax}.");

            return sb.ToString().Trim();
        }
    }
}
