# PreToolUse hook: blocks raw configuration reads and options binding outside the helper in .cs edits. Fails open on bad input.
try {
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
    $path = [string]$data.tool_input.file_path
    if ($path -notmatch '(?i)\.cs$') { exit 0 }

    $normalizedPath = $path.Replace('\', '/')
    $isOptionsHelper = $normalizedPath -match '(?i)Extensions/OptionsExtensions\.cs$'
    $texts = @($data.tool_input.new_string, $data.tool_input.content) | Where-Object { $_ }

    foreach ($text in $texts) {
        foreach ($line in ([string]$text -split "\r?\n")) {
            if ($line -match 'Configuration\[|GetSection\(|GetValue<') {
                [Console]::Error.WriteLine("Blocked: read settings through IOptions<T>, not raw configuration. See .claude/rules/configuration.md. Offending line: $($line.Trim())")
                exit 2
            }
        }
    }

    if (-not $isOptionsHelper) {
        foreach ($text in $texts) {
            foreach ($line in ([string]$text -split "\r?\n")) {
                if ($line -match 'BindConfiguration\(|ValidateOnStart\(') {
                    [Console]::Error.WriteLine("Blocked: bind and validate settings only through AddValidatedOptions<T> in Extensions/OptionsExtensions.cs. See .claude/rules/configuration.md. Offending line: $($line.Trim())")
                    exit 2
                }
            }
        }
    }
} catch {
    exit 0
}
exit 0
