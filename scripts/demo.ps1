param([string]$BaseUrl = 'http://localhost:8080')
$ErrorActionPreference = 'Stop'
function Get-Api([string]$Path) { Invoke-RestMethod "$BaseUrl/api/$Path" }
function Post-Api([string]$Path) { Invoke-RestMethod -Method Post "$BaseUrl/api/$Path" }

$campaign = Get-Api 'campaigns' | Where-Object id -eq '1001'
if ($campaign.status -ne 'Enabled') { throw 'This demonstration requires a fresh Compose database. See README reset instructions.' }
Write-Host "Before: $($campaign.name), $($campaign.status), spend $($campaign.spend), conversions $($campaign.conversions)"
$result = Post-Api 'evaluations/run'
if ($result.createdIncidents -ne 4 -or $result.completedActions -ne 3) { throw 'Unexpected evaluation result.' }
$campaign = Get-Api 'campaigns' | Where-Object id -eq '1001'
if ($campaign.status -ne 'Paused') { throw 'Automatic campaign was not paused.' }
$incidents = Get-Api 'incidents'
$automatic = $incidents | Where-Object campaignId -eq '1001'
$detail = Get-Api "incidents/$($automatic.id)"
if (-not ($detail.audit | Where-Object event -eq 'PauseCompleted')) { throw 'Missing completion audit.' }
Write-Host "Automatic: $($campaign.status), incident $($automatic.id), $($detail.audit.Count) audit entries"

$approval = $incidents | Where-Object campaignId -eq '1002'
if ($approval.status -ne 'PendingApproval') { throw 'Expected pending approval.' }
$approved = Post-Api "incidents/$($approval.id)/approve"
if ($approved.status -ne 'Executed') { throw 'Approval did not execute.' }
Write-Host "Approval: $($approved.status)"
$again = Post-Api 'evaluations/run'
if ($again.createdIncidents -ne 0 -or $again.completedActions -ne 0) { throw 'Duplicate work was created.' }
Write-Host 'Duplicate evaluation: no new incidents or actions. Demo passed.'
