param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$script = Join-Path $PSScriptRoot 'compile.py'

if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 $script @Arguments
}
elseif (Get-Command python -ErrorAction SilentlyContinue) {
    & python $script @Arguments
}
else {
    Write-Error 'Python 3 was not found. Install it from https://www.python.org/downloads/'
    exit 1
}

$code = $LASTEXITCODE
if ($Arguments.Count -eq 0) {
    Write-Host ''
    Read-Host 'Press Enter to exit'
}
exit $code
