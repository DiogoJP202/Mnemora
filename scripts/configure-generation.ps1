# Run from any directory. Secrets stay in the current user's .NET development store.
# The API reads this store only in Development; production uses its host's environment.
$ErrorActionPreference = 'Stop'
$taskProjectPath = Join-Path $PSScriptRoot '../src/Mnemora.Api/Mnemora.Api.csproj'
$taskSecret = $null
$taskKey = $null
$taskSettings = $null
$taskPointer = [IntPtr]::Zero

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Instale o SDK .NET 10 antes de configurar a geracao.'
    }
    Write-Host 'Configuracao local do Groq. Use uma organizacao Free, sem upgrade pago.'
    $taskSecret = Read-Host 'Cole a chave Groq (entrada oculta)' -AsSecureString
    $taskPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskSecret)
    $taskKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer).Trim()
    if ([string]::IsNullOrWhiteSpace($taskKey) -or $taskKey -match '\s|\p{C}') {
        throw 'Informe uma chave sem espacos ou quebras de linha no meio do valor.'
    }
    $taskSettings = @{
        'MemoryPackGeneration:Enabled' = 'true'
        'MemoryPackGeneration:Provider' = 'Groq'
        'MemoryPackGeneration:Model' = 'openai/gpt-oss-120b'
        'MemoryPackGeneration:ApiKey' = $taskKey
    }
    # Send JSON through stdin: the credential never appears in command arguments.
    # Suppress CLI output, including error output, to avoid printing submitted values.
    $taskSettings | ConvertTo-Json -Compress | & dotnet user-secrets set --project $taskProjectPath *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Nao foi possivel salvar a configuracao local com dotnet user-secrets.'
    }
    Write-Host 'Configuracao salva fora do repositorio. Nenhuma chamada ao Groq foi feita.'
    Write-Host 'No terminal da API, pressione Ctrl+C e execute novamente: dotnet run --project src/Mnemora.Api'
    Write-Host 'Variaveis de ambiente tem prioridade sobre user secrets. Remova valores antigos ou vazios desse terminal se houver conflito.'
    Write-Host 'Depois, use Verificar configuracao novamente no painel do livro.'
}
finally {
    if ($taskPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer)
    }
    if ($null -ne $taskSecret) { $taskSecret.Dispose() }
    $taskKey = $null
    $taskSettings = $null
}
