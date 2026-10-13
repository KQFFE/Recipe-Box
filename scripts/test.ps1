$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "RecipeBox.slnx"
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "RecipeBoxTests-$([Guid]::NewGuid().ToString('N'))"
$baseOutputPath = Join-Path $temporaryDirectory "bin\"
$testExitCode = 1

try {
    & dotnet test $solutionPath "-p:BaseOutputPath=$baseOutputPath"
    $testExitCode = $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

exit $testExitCode
