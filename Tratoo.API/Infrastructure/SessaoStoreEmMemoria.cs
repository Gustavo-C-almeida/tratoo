namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// <see cref="ISessaoStore"/> na memória do processo: testes e execução local sem Redis
    /// (uma réplica só). Com duas réplicas não serve — uma não enxergaria as sessões da
    /// outra —, por isso só é registrado quando <c>Redis:ConnectionString</c> não existe.
    ///
    /// A expiração usa o <see cref="TimeProvider"/> injetado: os testes avançam o relógio em
    /// vez de esperar. Um único lock torna cada operação atômica, como os comandos/scripts
    /// da versão Redis.
    /// </summary>
    public sealed class SessaoStoreEmMemoria : ISessaoStore
    {
        private readonly TimeProvider _relogio;
        private readonly object _trava = new();
        private readonly Dictionary<string, (object Valor, DateTimeOffset ExpiraEm)> _itens = new();
        private readonly Dictionary<int, HashSet<string>> _indices = new();

        public SessaoStoreEmMemoria(TimeProvider relogio) => _relogio = relogio;

        public Task CriarAsync(string sid, DadosSessao sessao, TimeSpan ttlSessao, string refreshToken, TimeSpan ttlRefresh)
        {
            lock (_trava)
            {
                Definir(ChavesSessao.Sessao(sid), sessao, ttlSessao);
                Definir(ChavesSessao.Refresh(refreshToken), sid, ttlRefresh);
                Indice(sessao.UsuarioId).Add(sid);
            }
            return Task.CompletedTask;
        }

        public Task<DadosSessao?> ObterAsync(string sid)
        {
            lock (_trava)
                return Task.FromResult(Obter<DadosSessao>(ChavesSessao.Sessao(sid)));
        }

        public Task<bool> AtualizarSeExisteAsync(string sid, DadosSessao sessao, TimeSpan ttl)
        {
            lock (_trava)
            {
                var chave = ChavesSessao.Sessao(sid);
                if (Obter<DadosSessao>(chave) is null)
                    return Task.FromResult(false);

                Definir(chave, sessao, ttl);
                return Task.FromResult(true);
            }
        }

        public Task<bool> RemoverAsync(string sid, int usuarioId)
        {
            lock (_trava)
            {
                var existia = Obter<DadosSessao>(ChavesSessao.Sessao(sid)) is not null;
                _itens.Remove(ChavesSessao.Sessao(sid));
                _itens.Remove(ChavesSessao.Reauth(sid));
                Indice(usuarioId).Remove(sid);
                return Task.FromResult(existia);
            }
        }

        public Task<IReadOnlyList<(string Sid, DadosSessao Sessao)>> ListarDoUsuarioAsync(int usuarioId)
        {
            lock (_trava)
            {
                var indice = Indice(usuarioId);
                var vivas = new List<(string, DadosSessao)>();
                foreach (var sid in indice.ToList())
                {
                    var sessao = Obter<DadosSessao>(ChavesSessao.Sessao(sid));
                    if (sessao is null || sessao.UsuarioId != usuarioId)
                        indice.Remove(sid);
                    else
                        vivas.Add((sid, sessao));
                }
                return Task.FromResult<IReadOnlyList<(string, DadosSessao)>>(vivas);
            }
        }

        public Task<string?> ObterSidDoRefreshAsync(string refreshToken)
        {
            lock (_trava)
                return Task.FromResult(Obter<string>(ChavesSessao.Refresh(refreshToken)));
        }

        public Task<bool> ConsumirRefreshAsync(string refreshToken, string sid, RefreshUsado marca, TimeSpan ttlMarca)
        {
            lock (_trava)
            {
                var chave = ChavesSessao.Refresh(refreshToken);
                if (Obter<string>(chave) != sid)
                    return Task.FromResult(false);

                _itens.Remove(chave);
                Definir(ChavesSessao.RefreshUsado(refreshToken), marca, ttlMarca);
                return Task.FromResult(true);
            }
        }

        public Task GravarRefreshAsync(string refreshToken, string sid, TimeSpan ttl)
        {
            lock (_trava)
                Definir(ChavesSessao.Refresh(refreshToken), sid, ttl);
            return Task.CompletedTask;
        }

        public Task<RefreshUsado?> ObterRefreshUsadoAsync(string refreshToken)
        {
            lock (_trava)
                return Task.FromResult(Obter<RefreshUsado>(ChavesSessao.RefreshUsado(refreshToken)));
        }

        public Task MarcarReautenticacaoAsync(string sid, TimeSpan ttl)
        {
            lock (_trava)
                Definir(ChavesSessao.Reauth(sid), true, ttl);
            return Task.CompletedTask;
        }

        public Task<bool> ReautenticouRecentementeAsync(string sid)
        {
            lock (_trava)
                return Task.FromResult(Obter<object>(ChavesSessao.Reauth(sid)) is true);
        }

        // ── Auxiliares (chamados com a trava) ───────────────────────────────────

        private void Definir(string chave, object valor, TimeSpan ttl) =>
            _itens[chave] = (valor, _relogio.GetUtcNow() + ttl);

        private T? Obter<T>(string chave) where T : class
        {
            if (!_itens.TryGetValue(chave, out var item))
                return null;

            if (_relogio.GetUtcNow() >= item.ExpiraEm)
            {
                _itens.Remove(chave);
                return null;
            }
            return item.Valor as T;
        }

        private HashSet<string> Indice(int usuarioId)
        {
            if (!_indices.TryGetValue(usuarioId, out var indice))
                _indices[usuarioId] = indice = new HashSet<string>();
            return indice;
        }
    }
}
