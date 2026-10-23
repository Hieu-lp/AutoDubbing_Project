[CmdletBinding()]
param(
    [switch]$IncludeLockTest,
    [switch]$TrustDevelopmentCertificate,
    [switch]$PauseAtEnd
)

$ErrorActionPreference = 'Stop'
$baseUri = 'https://localhost:7239/api/admin/auth'
$workspacePath = Split-Path -Parent $PSScriptRoot
$reportDirectory = Join-Path $workspacePath 'artifacts/test-results'
$reportPath = Join-Path $reportDirectory 'admin-auth-sqlserver.json'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

$checks = New-Object 'System.Collections.Generic.List[object]'
$report = [ordered]@{
    StartedAtUtc = [DateTime]::UtcNow.ToString('o')
    State = 'Starting'
    Checks = @()
    LockTestRequested = [bool]$IncludeLockTest
    AccountRestored = $null
    Error = $null
}
$restoreRequired = $false
$adminId = 0
$stage = 'startup'
$credential = $null
$plainPassword = $null
$token = $null
$loginResponse = $null
$sqlcmd = $null

function Save-Report {
    $report.Checks = @($checks.ToArray())
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}

function Add-Check {
    param([string]$Name, [int]$Expected, [int]$Actual)
    $checks.Add([pscustomobject]@{
        Name = $Name
        ExpectedHttp = $Expected
        ActualHttp = $Actual
        Passed = ($Expected -eq $Actual)
    })
    Write-Host ('{0}: HTTP {1} (expected {2})' -f $Name, $Actual, $Expected)
    Save-Report
}

function Invoke-AuthRequest {
    param([string]$Method, [string]$Path, [hashtable]$Payload, [string]$AccessToken)
    $request = @{
        Uri = "$baseUri/$Path"
        Method = $Method
        UseBasicParsing = $true
        MaximumRedirection = 0
        TimeoutSec = 15
        ErrorAction = 'Stop'
    }
    $bodyText = $null
    try {
        if ($Payload) {
            $bodyText = $Payload | ConvertTo-Json -Compress
            $request.ContentType = 'application/json; charset=utf-8'
            $request.Body = [Text.Encoding]::UTF8.GetBytes($bodyText)
        }
        if ($AccessToken) {
            $request.Headers = @{ Authorization = "Bearer $AccessToken" }
        }
        $response = Invoke-WebRequest @request
        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Content = $response.Content }
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            # Loi HTTP khong can luu response body (tranh luu token/thong tin tai khoan).
            return [pscustomobject]@{ StatusCode = [int]$_.Exception.Response.StatusCode; Content = $null }
        }
        throw 'Khong ket noi HTTPS duoc. Kiem tra API va trust chung chi; khong bo qua TLS validation.'
    }
    finally {
        $bodyText = $null
        $request = $null
        $Payload = $null
        $AccessToken = $null
    }
}

function Set-AccountStatus {
    param([int]$Id, [ValidateSet('Active', 'Locked')][string]$Status)
    if ($Id -le 0) { throw 'Admin ID khong hop le; khong cap nhat database.' }
    # Chi co int ID va hai hang trang thai duoc dua vao SQL, khong co password/hash/token.
    if ($Status -eq 'Locked') {
        $query = "SET NOCOUNT ON; UPDATE dbo.NGUOI_DUNG SET trang_thai=N'Locked' WHERE id=$Id AND trang_thai=N'Active'; IF @@ROWCOUNT<>1 THROW 51200, 'Account is not Active or does not exist.', 1;"
    }
    else {
        $query = "SET NOCOUNT ON; UPDATE dbo.NGUOI_DUNG SET trang_thai=N'Active' WHERE id=$Id; IF @@ROWCOUNT<>1 THROW 51201, 'Account does not exist.', 1;"
    }
    $sqlOutput = & $sqlcmd.Source -S . -d AutoDubbingDb -E -C -l 5 -t 10 -b -Q $query 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Khong cap nhat duoc trang thai tai khoan tren SQL Server.' }
}

try {
    Save-Report
    if ($TrustDevelopmentCertificate) {
        $stage = 'trust-development-certificate'
        Write-Host 'Windows co the hoi xac nhan trust chung chi HTTPS local. Hay chap nhan de kiem tra.'
        & dotnet dev-certs https --trust
        if ($LASTEXITCODE -ne 0) { throw 'Chua trust duoc chung chi HTTPS.' }
    }

    $stage = 'https-preflight'
    $missing = Invoke-AuthRequest -Method GET -Path me
    Add-Check -Name 'MissingToken' -Expected 401 -Actual $missing.StatusCode
    $invalid = Invoke-AuthRequest -Method GET -Path me -AccessToken 'not-a-valid-jwt'
    Add-Check -Name 'InvalidToken' -Expected 401 -Actual $invalid.StatusCode
    if ($missing.StatusCode -ne 401 -or $invalid.StatusCode -ne 401) {
        throw 'Preflight HTTP khong dat.'
    }

    $stage = 'local-credential-input'
    $report.State = 'AwaitingLocalCredential'
    Save-Report
    Write-Host 'Nhap tai khoan Admin da tao. Khong gui mat khau vao chat. Report chi luu ma HTTP.'
    $credential = Get-Credential -Message 'Admin da tao trong AutoDubbingDb - nhap mat khau tai may nay'
    if ($null -eq $credential) { throw 'Da huy nhap tai khoan.' }
    $plainPassword = $credential.GetNetworkCredential().Password
    $report.State = 'Running'
    Save-Report

    $stage = 'valid-login'
    $loginResponse = Invoke-AuthRequest -Method POST -Path login -Payload @{
        username = $credential.UserName
        password = $plainPassword
    }
    Add-Check -Name 'ValidLogin' -Expected 200 -Actual $loginResponse.StatusCode
    if ($loginResponse.StatusCode -ne 200) { throw 'Dang nhap khong thanh cong; khong chay ca khoa tai khoan.' }
    $loginData = $loginResponse.Content | ConvertFrom-Json
    $loginFields = @($loginData.PSObject.Properties.Name) + @($loginData.admin.PSObject.Properties.Name)
    if (@($loginFields | Where-Object { $_ -match '(?i)^password' }).Count -gt 0) {
        throw 'Response dang nhap chua truong password.'
    }
    $token = [string]$loginData.accessToken
    $adminId = [int]$loginData.admin.id
    if ([string]::IsNullOrWhiteSpace($token) -or $adminId -le 0) { throw 'Response dang nhap thieu token/ID.' }
    $loginResponse = $null
    $loginData = $null

    $stage = 'authenticated-me'
    $me = Invoke-AuthRequest -Method GET -Path me -AccessToken $token
    Add-Check -Name 'AuthenticatedMe' -Expected 200 -Actual $me.StatusCode
    if ($me.StatusCode -ne 200) { throw 'Response /me khong dat.' }
    $meData = $me.Content | ConvertFrom-Json
    if (@($meData.PSObject.Properties.Name | Where-Object { $_ -match '(?i)^password' }).Count -gt 0) {
        throw 'Response /me chua truong password.'
    }
    if ([int]$meData.id -ne $adminId) { throw '/me tra ve sai tai khoan.' }
    $meData = $null
    $me = $null

    $stage = 'wrong-password'
    do { $wrongPassword = [Guid]::NewGuid().ToString('N') } while ($wrongPassword -ceq $plainPassword)
    $wrong = Invoke-AuthRequest -Method POST -Path login -Payload @{
        username = $credential.UserName
        password = $wrongPassword
    }
    Add-Check -Name 'WrongPassword' -Expected 401 -Actual $wrong.StatusCode
    $wrongPassword = $null

    if ($IncludeLockTest) {
        $stage = 'lock-account'
        $sqlcmd = Get-Command sqlcmd -ErrorAction Stop
        Write-Host 'Dang tam khoa tai khoan vua dang nhap de kiem tra. Giu cua so mo den khi hoan tat.'
        try {
            Set-AccountStatus -Id $adminId -Status Locked
        }
        catch {
            # Khong tu mo khoa neu UPDATE khong xac nhan thanh cong (tai khoan co the da Locked tu truoc).
            # Neu mat ket noi ngay sau commit, ID nay giup nguoi van han kiem tra/khoi phuc thu cong.
            $report.RecoveryAdminId = $adminId
            throw
        }
        $restoreRequired = $true
        try {
            $stage = 'locked-login'
            $lockedLogin = Invoke-AuthRequest -Method POST -Path login -Payload @{
                username = $credential.UserName
                password = $plainPassword
            }
            Add-Check -Name 'LockedLogin' -Expected 401 -Actual $lockedLogin.StatusCode
            $stage = 'locked-me'
            $lockedMe = Invoke-AuthRequest -Method GET -Path me -AccessToken $token
            Add-Check -Name 'LockedMeWithExistingToken' -Expected 401 -Actual $lockedMe.StatusCode
        }
        finally {
            Set-AccountStatus -Id $adminId -Status Active
            $restoreRequired = $false
            $report.AccountRestored = $true
            Write-Host 'Da dua tai khoan ve Active.'
        }
        $stage = 'restored-me'
        $restoredMe = Invoke-AuthRequest -Method GET -Path me -AccessToken $token
        Add-Check -Name 'RestoredMe' -Expected 200 -Actual $restoredMe.StatusCode
    }

    $report.State = 'Completed'
    if (@($checks | Where-Object { -not $_.Passed }).Count -gt 0) { $report.State = 'Failed' }
}
catch {
    $report.State = 'Failed'
    # Khong ghi raw exception, request/response, credential hay token ra file.
    $report.Error = 'Verification stopped at stage: ' + $stage
    Write-Host $report.Error -ForegroundColor Red
}
finally {
    if ($restoreRequired) {
        try {
            Set-AccountStatus -Id $adminId -Status Active
            $report.AccountRestored = $true
        }
        catch {
            $report.AccountRestored = $false
            $report.RecoveryAdminId = $adminId
            $report.State = 'Failed'
            Write-Host "Can khoi phuc trang thai Active cho Admin ID $adminId trong SQL Server." -ForegroundColor Red
        }
    }
    $plainPassword = $null
    $credential = $null
    $token = $null
    $loginResponse = $null
    $report.FinishedAtUtc = [DateTime]::UtcNow.ToString('o')
    Save-Report
    Write-Host ('Result: ' + $report.State)
    Write-Host ('Report (khong co secret): ' + $reportPath)
    if ($PauseAtEnd) { Read-Host 'Nhan Enter de dong cua so' | Out-Null }
}
