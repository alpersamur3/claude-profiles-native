$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/Tests.csproj -c Release -- --scratch (Join-Path $env:TEMP ('ClaudeProfilesNative-tests-' + [Guid]::NewGuid().ToString('N')))
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
