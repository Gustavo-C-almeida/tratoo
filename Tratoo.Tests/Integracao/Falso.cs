using System.Reflection;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Implementação "não faz nada" de qualquer interface, gerada em tempo de execução
    /// (DispatchProxy, do próprio .NET). Serve para preencher dependências que o teste não
    /// exercita — e-mail, PDF, storage — sem escrever uma classe falsa para cada uma.
    /// Métodos com comportamento relevante ao teste são sobrescritos por nome.
    /// </summary>
    public class Falso<T> : DispatchProxy where T : class
    {
        private IReadOnlyDictionary<string, Func<object?[], object?>> _respostas =
            new Dictionary<string, Func<object?[], object?>>();

        public static T Criar(IReadOnlyDictionary<string, Func<object?[], object?>>? respostas = null)
        {
            var proxy = Create<T, Falso<T>>();
            ((Falso<T>)(object)proxy)._respostas = respostas ?? new Dictionary<string, Func<object?[], object?>>();
            return proxy;
        }

        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            if (metodo is null)
                return null;

            if (_respostas.TryGetValue(metodo.Name, out var resposta))
                return resposta(args ?? Array.Empty<object?>());

            var retorno = metodo.ReturnType;
            if (retorno == typeof(void))
                return null;
            if (retorno == typeof(Task))
                return Task.CompletedTask;
            if (retorno.IsGenericType && retorno.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var tipo = retorno.GetGenericArguments()[0];
                var padrao = tipo.IsValueType ? Activator.CreateInstance(tipo) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(tipo)
                    .Invoke(null, new[] { padrao });
            }
            return retorno.IsValueType ? Activator.CreateInstance(retorno) : null;
        }
    }
}
