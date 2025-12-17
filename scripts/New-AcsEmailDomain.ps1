param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$EmailServiceName,

    [Parameter(Mandatory = $true)]
    [string]$DomainName,
    
    [Parameter(Mandatory = $false)]
    [switch]$InitiateVerification
)
Import-Module Az.Communication -ErrorAction Stop

if (-not (Get-AzContext)) {
    Connect-AzAccount -ErrorAction Stop | Out-Null
}

function Add-CnameRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)]
        [string]$DomainName,
        [Parameter(Mandatory = $true)]
        [string]$RecordName,
        [Parameter(Mandatory = $true)]
        [string]$RecordValue
    )
    $RecordSet = Get-AzDnsRecordSet -ResourceGroupName $ResourceGroupName -ZoneName $DomainName -RecordType CNAME -Name $RecordName -ErrorAction SilentlyContinue

    if (-not $RecordSet) {
        Write-Verbose "Creating new DNS CNAME record for $RecordName with value $RecordValue."
        New-AzDnsRecordSet -RecordType CNAME `
            -ResourceGroupName $ResourceGroupName `
            -Ttl 3600 `
            -ZoneName $DomainName `
            -Name $RecordName `
            -DnsRecords (New-AzDnsRecordConfig -Cname $RecordValue)
    }
    else {
        Write-Verbose "Updating DNS CNAME record for $RecordName with value $RecordValue."
        $RecordSet.Records[0].Cname = $RecordValue
        Set-AzDnsRecordSet -RecordSet $RecordSet > $null
    }

}   
function Add-TxtRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,
        [Parameter(Mandatory = $true)]
        [string]$DomainName,
        [Parameter(Mandatory = $false)]
        [string]$RecordName = "@",
        [Parameter(Mandatory = $true)]
        [string]$RecordValue
    )
    $RecordSet = Get-AzDnsRecordSet -ResourceGroupName $ResourceGroupName -ZoneName $DomainName -RecordType TXT -Name $RecordName -ErrorAction SilentlyContinue

    if (-not $RecordSet) {
        Write-Verbose "Creating new DNS TXT record for $RecordName with value $RecordValue."
        New-AzDnsRecordSet -RecordType TXT `
            -ResourceGroupName $ResourceGroupName `
            -Ttl 3600 `
            -ZoneName $DomainName `
            -Name $RecordName `
            -DnsRecords (New-AzDnsRecordConfig -Value $RecordValue)
    }
    else {
        Write-Verbose "Updating DNS TXT record for $RecordName with value $RecordValue."
        $ExistingRecord = $RecordSet.Records | Where-Object { $_.Value -contains $RecordValue}
        
        if (-not $ExistingRecord) {
            Write-Verbose "Adding new DNS TXT record to $RecordName with value $RecordValue."
            $RecordSet.Records.Add((New-AzDnsRecordConfig -Value $RecordValue))
            Set-AzDnsRecordSet -RecordSet $RecordSet > $null
        }
    }

}

$EmailService = Get-AzEmailService -ResourceGroupName $ResourceGroupName -Name $EmailServiceName -ErrorAction SilentlyContinue

if (-not $EmailService) {
    throw "Email Service '$EmailServiceName' not found in Resource Group '$ResourceGroupName'."
}

$Domain = Get-AzEmailServiceDomain -ResourceGroupName $ResourceGroupName -EmailServiceName $EmailServiceName -Name $DomainName

if (-not $Domain) {
    $Domain = New-AzEmailServiceDomain `
        -ResourceGroupName $ResourceGroupName `
        -EmailServiceName $EmailServiceName `
        -Name $DomainName `
        -DomainManagement CustomerManaged
}

# Add DNS TXT record for domain verification, SPF, DKIM, and DMARC as needed.

$Verification = ($Domain | Select-Object -ExpandProperty VerificationRecord).ToJsonString() | ConvertFrom-Json


# Domain Verification
Add-TxtRecord `
    -ResourceGroupName $ResourceGroupName `
    -DomainName $DomainName `
    -RecordName "@" `
    -RecordValue $Verification.Domain.Value

# SPF Record
Add-TxtRecord `
    -ResourceGroupName $ResourceGroupName `
    -DomainName $DomainName `
    -RecordName "@" `
    -RecordValue $Verification.SPF.Value

# DKIM Records
Add-CnameRecord `
    -ResourceGroupName $ResourceGroupName `
    -DomainName $DomainName `
    -RecordName $Verification.DKIM.Name `
    -RecordValue $Verification.DKIM.Value

# DKIM2 Records
Add-CnameRecord `
    -ResourceGroupName $ResourceGroupName `
    -DomainName $DomainName `
    -RecordName $Verification.DKIM2.Name `
    -RecordValue $Verification.DKIM2.Value

# DKIM2 Record
Add-CnameRecord `
    -ResourceGroupName $ResourceGroupName `
    -DomainName $DomainName `
    -RecordName $Verification.DKIM2.Name `
    -RecordValue $Verification.DKIM2.Value

if ($InitiateVerification.IsPresent) {

    Invoke-AzEmailServiceInitiateDomainVerification `
        -ResourceGroupName $ResourceGroupName `
        -EmailServiceName $EmailServiceName `
        -DomainName $DomainName `
        -VerificationType Domain  

    
    Invoke-AzEmailServiceInitiateDomainVerification `
        -ResourceGroupName $ResourceGroupName `
        -EmailServiceName $EmailServiceName `
        -DomainName $DomainName `
        -VerificationType DKIM 

    Invoke-AzEmailServiceInitiateDomainVerification `
        -ResourceGroupName $ResourceGroupName `
        -EmailServiceName $EmailServiceName `
        -DomainName $DomainName `
        -VerificationType DKIM2 
    
    Invoke-AzEmailServiceInitiateDomainVerification `
        -ResourceGroupName $ResourceGroupName `
        -EmailServiceName $EmailServiceName `
        -DomainName $DomainName `
        -VerificationType SPF 
}