$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotnet = Join-Path $root ".dotnet\dotnet.exe"

if (Test-Path $localDotnet) {
    $dotnet = $localDotnet
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw "O SDK do .NET 10 não foi encontrado."
    }

    $dotnet = $dotnetCommand.Source
}

$solution = Join-Path $root "Ludoteca.sln"
$project = Join-Path $root "Ludoteca\Ludoteca.csproj"
$output = Join-Path $root "dist\JOGOTECA-win-x64"

& $dotnet test $solution -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Os testes falharam."
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "A publicação falhou."
}

Write-Host ""
Write-Host "JOGOTECA publicada em:"
Write-Host (Join-Path $output "JOGOTECA.exe")
