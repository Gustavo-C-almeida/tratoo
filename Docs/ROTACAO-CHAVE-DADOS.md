# Rotação da chave de dados (`DataProtector`)

A chave AES de `DataProtector` cifra conta bancária, chave PIX, CPF/CNPJ e CPF do
representante legal (colunas `ContasBancarias.PixChave`, `ContasBancarias.ContaCriptografada`,
`UserIdentities.CpfCnpjCriptografado`, `UserIdentities.ChavePixCriptografada` e
`UserIdentities.CpfRepresentanteLegalCriptografado`). Trocar a variável sozinha torna esses
dados ilegíveis: a troca é feita em etapas, sem janela de indisponibilidade.

## Como funciona

| Configuração | Papel durante a rotação |
|---|---|
| `DataProtector__Chave` | chave **nova**: tudo o que é gravado usa ela |
| `DataProtector__ChaveAnterior` | chave **antiga**: só para ler o que ainda não foi recifrado |

- Leitura: tenta a nova; se não decifrar, a anterior.
- `dotnet Tratoo.API.dll --recifrar-dados`: relatório por coluna (quantos valores em cada
  chave), sem gravar nada.
- `dotnet Tratoo.API.dll --recifrar-dados --aplicar`: regrava com a nova tudo o que está na
  anterior, numa transação, e relê o banco para conferir. Idempotente. Não registra nenhum
  valor, só contagens.
- `/health/ready`, check `dados-cifrados`: `Degraded` (HTTP 200) com
  "Rotação de chave em curso: N de M amostras ainda na chave anterior" até a recifragem
  terminar; depois, `Healthy`.

Salvaguardas: a API não sobe se `ChaveAnterior` for igual à `Chave`; um valor que não decifra
com nenhuma das duas (ou, caso praticamente impossível, com as duas) é contado e deixado como
está; um valor alterado pela API durante a recifragem desfaz a transação inteira (basta rodar
de novo).

## Roteiro em produção (Railway)

Pré-requisito: a versão com suporte a `ChaveAnterior` já está no ar (sem `ChaveAnterior`
configurada ela se comporta exatamente como antes).

Os comandos são PowerShell. **A chave nunca é exibida**: ela fica numa variável da sessão e
vai para a Railway pelo stdin. O stdin do PowerShell 5.1 acrescenta BOM e quebra de linha, que
mudariam a chave; a função abaixo grava os bytes exatos.

```powershell
function Set-RailwayVar([string]$nome, [string]$valor, [switch]$SemDeploy) {
  $exe = Join-Path $env:APPDATA 'npm\node_modules\@railway\cli\bin\railway.exe'
  $anterior = [Console]::InputEncoding
  try {
    [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)   # sem BOM
    $psi = New-Object System.Diagnostics.ProcessStartInfo $exe
    $psi.Arguments = "variable set $nome --stdin" + $(if ($SemDeploy) { ' --skip-deploys' } else { '' })
    $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.Write($valor); $p.StandardInput.Close(); $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "railway variable set $nome falhou ($($p.ExitCode))" }
  } finally { [Console]::InputEncoding = $anterior }
}
```

1. **Ler a chave atual e gerar a nova** (na mesma sessão do PowerShell):
   ```powershell
   $antiga = [string](railway variables --json | ConvertFrom-Json).DataProtector__Chave
   $bytes = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
   $nova = [Convert]::ToBase64String($bytes)   # 32 caracteres = 192 bits aleatórios
   "antiga: $($antiga.Length) caracteres; nova: $($nova.Length) caracteres"
   ```
   Guarde a nova também num cofre de senhas (é a única cópia fora da Railway).

2. **Configurar as duas chaves**. A anterior primeiro e sem deploy; a nova em seguida dispara
   **um** deploy com as duas:
   ```powershell
   Set-RailwayVar DataProtector__ChaveAnterior $antiga -SemDeploy
   Set-RailwayVar DataProtector__Chave $nova
   ```
   Se o deploy subisse só com a nova, o `dados-cifrados` ficaria `Unhealthy` e a versão não
   seria promovida: a ordem acima evita isso.

3. **Conferir as variáveis** (imprime só IGUAL/DIFERENTE):
   ```powershell
   $v = railway variables --json | ConvertFrom-Json
   "Chave = nova: "        + [string]::Equals([string]$v.DataProtector__Chave, $nova, 'Ordinal')
   "ChaveAnterior = antiga: " + [string]::Equals([string]$v.DataProtector__ChaveAnterior, $antiga, 'Ordinal')
   ```
   Use sempre comparação `Ordinal`: `-eq`/`-ceq` ignoram BOM e dariam falso "igual".

4. **Esperar o deploy** e conferir `https://www.tratoo.app.br/health/ready`: `dados-cifrados`
   `Degraded` com "ainda na chave anterior" (ou `Healthy`, se não houver dado cifrado).

5. **Recifrar** dentro do contêiner (as variáveis do serviço estão disponíveis na sessão SSH):
   ```powershell
   railway ssh "cd /app && dotnet Tratoo.API.dll --recifrar-dados"
   railway ssh "cd /app && dotnet Tratoo.API.dll --recifrar-dados --aplicar"
   ```
   Esperado no segundo: `Recifragem concluída: todos os valores cifrados estão na chave atual`
   e código de saída 0. Código 3 = sobraram valores ilegíveis ou ambíguos: **não** siga para o
   passo 6 antes de entendê-los.

6. **Retirar a chave anterior** (dispara um deploy):
   ```powershell
   railway variable delete DataProtector__ChaveAnterior
   ```
   Conferir `/health/ready`: `dados-cifrados` `Healthy`, "N de N amostras decifradas".

7. **Descartar a chave antiga**: apagar cópias locais e backups que a contenham. Ela continua
   comprometida para sempre; o que importa é que nenhum dado depende mais dela.

## Voltar atrás

- **Antes do passo 5**: restaurar `DataProtector__Chave` com a antiga e apagar
  `DataProtector__ChaveAnterior`. Nada foi regravado; o que a API cifrou com a nova nesse
  intervalo fica ilegível. Por isso, para desfazer, prefira a opção seguinte.
- **Depois do passo 5**: a rotação é simétrica. Inverter as variáveis (`Chave` = antiga,
  `ChaveAnterior` = nova) e rodar `--recifrar-dados --aplicar` de novo.

## Ambiente local (compose)

`DATAPROTECTOR_CHAVE_ANTERIOR` no `.env` corresponde a `DataProtector__ChaveAnterior`; vazia,
não há rotação. A recifragem roda com
`docker compose run --rm api --recifrar-dados` (e `--recifrar-dados --aplicar`), com a stack no ar;
o `ENTRYPOINT` da imagem já é `dotnet Tratoo.API.dll`.
