[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Path,
    [Parameter(Mandatory)] [string] $ExpectedSubject,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9A-Fa-f]{40}$')] [string] $ExpectedThumbprint,
    [switch] $RequireTimestamp
)

$expected = $ExpectedThumbprint.ToUpperInvariant()
foreach ($item in $Path) {
    $resolved = (Resolve-Path -LiteralPath $item -ErrorAction Stop).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $resolved
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "$resolved has invalid Authenticode status: $($signature.Status)"
    }
    if ($signature.SignerCertificate.Subject -ne $ExpectedSubject) {
        throw "$resolved signer subject does not match the approved identity."
    }
    if ($signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $expected) {
        throw "$resolved signer thumbprint does not match the approved identity."
    }
    if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) {
        throw "$resolved has no Authenticode timestamp countersignature."
    }

    [pscustomobject]@{
        Path = $resolved
        Status = $signature.Status.ToString()
        Subject = $signature.SignerCertificate.Subject
        Thumbprint = $signature.SignerCertificate.Thumbprint
        TimestampSubject = $signature.TimeStamperCertificate.Subject
    }
}
