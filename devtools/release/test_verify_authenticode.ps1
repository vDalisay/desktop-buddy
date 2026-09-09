$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'verify_authenticode.ps1'
$signed = Join-Path $env:SystemRoot 'System32\cmd.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $signed
if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
    throw 'Windows signed fixture is unavailable.'
}

& $verifier -Path $signed -ExpectedSubject $signature.SignerCertificate.Subject `
    -ExpectedThumbprint $signature.SignerCertificate.Thumbprint -RequireTimestamp | Out-Null

function Assert-Rejected([string] $Name, [scriptblock] $Action, [string] $Expected) {
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $Expected) { throw }
        return
    }
    throw "$Name was unexpectedly accepted."
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ("desktop-buddy-signature-test-" + [guid]::NewGuid())
New-Item -ItemType Directory $temporary | Out-Null
try {
    $unsigned = Join-Path $temporary 'unsigned.exe'
    [IO.File]::WriteAllBytes($unsigned, [byte[]](1, 2, 3, 4))
    Assert-Rejected 'unsigned file' {
        & $verifier -Path $unsigned -ExpectedSubject $signature.SignerCertificate.Subject `
            -ExpectedThumbprint $signature.SignerCertificate.Thumbprint -RequireTimestamp
    } 'invalid Authenticode status'

    Assert-Rejected 'wrong identity' {
        & $verifier -Path $signed -ExpectedSubject $signature.SignerCertificate.Subject `
            -ExpectedThumbprint ('0' * 40) -RequireTimestamp
    } 'thumbprint does not match'

    $tampered = Join-Path $temporary 'tampered.exe'
    Copy-Item -LiteralPath $signed -Destination $tampered
    $bytes = [IO.File]::ReadAllBytes($tampered)
    $bytes[1024] = $bytes[1024] -bxor 1
    [IO.File]::WriteAllBytes($tampered, $bytes)
    Assert-Rejected 'tampered file' {
        & $verifier -Path $tampered -ExpectedSubject $signature.SignerCertificate.Subject `
            -ExpectedThumbprint $signature.SignerCertificate.Thumbprint -RequireTimestamp
    } 'invalid Authenticode status'
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}

'Authenticode verifier tests passed.'
