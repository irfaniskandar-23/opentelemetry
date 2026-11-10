# PreToolUse hook: before a git commit, reminds Claude to update a rule file when code in its area changed but the rule did not. Never blocks; fails open.
$areas = [ordered]@{
    '.claude/rules/configuration.md'   = @(
        'src/OpenTelemetry.Api/Configuration/',
        'src/OpenTelemetry.Api/Extensions/ConfigurationExtensions.cs',
        'src/OpenTelemetry.Api/Extensions/OptionsExtensions.cs',
        'src/OpenTelemetry.Api/appsettings')
    '.claude/rules/controllers.md'     = @(
        'src/OpenTelemetry.Api/Controllers/',
        'src/OpenTelemetry.Api/Models/')
    '.claude/rules/program-startup.md' = @(
        'src/OpenTelemetry.Api/Program.cs',
        'src/OpenTelemetry.Api/Extensions/')
}

try {
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
    $command = [string]$data.tool_input.command
    if ($command -notmatch '\bgit\s+commit\b') { exit 0 }

    $root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { [string]$data.cwd }
    $changed = @(git -C $root status --porcelain=v1 -uall 2>$null | ForEach-Object {
        $path = $_.Substring(3)
        if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
        $path.Trim('"')
    })

    $reminders = foreach ($rule in $areas.Keys) {
        if ($changed -contains $rule) { continue }
        $hits = @($changed | Where-Object {
            $file = $_
            $areas[$rule] | Where-Object { $file.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }
        })
        if ($hits.Count -gt 0) { "$rule (changed: $($hits -join ', '))" }
    }

    if ($reminders) {
        $text = "Rule check before commit: code changed in the area of $($reminders -join '; '), but the rule file did not change. If this commit sets or changes a convention, update the rule in the same commit. If it only follows the existing rule, carry on."
        @{
            systemMessage      = $text
            hookSpecificOutput = @{ hookEventName = 'PreToolUse'; additionalContext = $text }
        } | ConvertTo-Json -Compress
    }
} catch {
    exit 0
}
exit 0
