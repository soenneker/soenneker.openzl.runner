$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installation)) {
    throw 'Could not locate Visual Studio with x64 C++ build tools.'
}

$previousEnvironment = @{}
Get-ChildItem Env: | ForEach-Object { $previousEnvironment[$_.Name] = $_.Value }

$developerShell = Join-Path $installation 'Common7/Tools/Launch-VsDevShell.ps1'
& $developerShell -Arch amd64 -HostArch amd64 -SkipAutomaticLocation

foreach ($variable in Get-ChildItem Env:) {
    if ($variable.Name -notmatch '^(GITHUB_|RUNNER_)' -and
        $previousEnvironment[$variable.Name] -cne $variable.Value) {
        "$($variable.Name)=$($variable.Value)" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
    }
}

foreach ($tool in 'clang-cl', 'cmake', 'ninja') {
    Get-Command $tool -ErrorAction Stop | Select-Object Name, Source
}
