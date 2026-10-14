$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProjects = @(
    (Join-Path $repositoryRoot "tests\RecipeBox.Tests\RecipeBox.Tests.csproj"),
    (Join-Path $repositoryRoot "tests\RecipeBox.BrowserTests\RecipeBox.BrowserTests.csproj")
)
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "RecipeBoxTests-$([Guid]::NewGuid().ToString('N'))"
$baseOutputPath = Join-Path $temporaryDirectory "bin\"
$testExitCode = 0

try {
    foreach ($testProject in $testProjects) {
        & dotnet test $testProject --configuration Release "-p:BaseOutputPath=$baseOutputPath"
        if ($LASTEXITCODE -ne 0) {
            $testExitCode = $LASTEXITCODE
            break
        }
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

exit $testExitCode
