<#
.SYNOPSIS
    Prints the ID of the oldest open Production incident that Claude has not claimed yet.
.DESCRIPTION
    Prints nothing when there is none. Needs a Better Stack API token in BETTERSTACK_API_TOKEN.
.LINK
    https://betterstack.com/docs/uptime/api/list-all-incidents/
.LINK
    https://betterstack.com/docs/uptime/api/list-all-comments/
#>

$ErrorActionPreference = 'Stop'

$apiUrl = 'https://incidents.betterstack.com/api'
$incidentNamePrefix = 'Production failure:'
$claimMarker = '**Investigating** (Claude'

if (-not $env:BETTERSTACK_API_TOKEN) {
    throw 'BETTERSTACK_API_TOKEN is not set.'
}

$headers = @{ Authorization = "Bearer $env:BETTERSTACK_API_TOKEN" }

$openIncidents = (Invoke-RestMethod -Uri "$apiUrl/v3/incidents?resolved=false&per_page=50" -Headers $headers).data |
    Where-Object { ([string]$_.attributes.name).StartsWith($incidentNamePrefix, [StringComparison]::Ordinal) } |
    Sort-Object { $_.attributes.started_at }

foreach ($incident in $openIncidents) {
    $comments = (Invoke-RestMethod -Uri "$apiUrl/v2/incidents/$($incident.id)/comments" -Headers $headers).data

    $claimComments = @($comments | Where-Object {
        ([string]$_.attributes.content).StartsWith($claimMarker, [StringComparison]::Ordinal)
    })

    if ($claimComments.Count -eq 0) {
        return $incident.id
    }
}
