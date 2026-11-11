# PreToolUse hook: blocks whole-line '//' comments in .cs edits. Fails open on bad input.
try {
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
    $path = [string]$data.tool_input.file_path
    if ($path -notmatch '(?i)\.cs$') { exit 0 }

    $texts = @($data.tool_input.new_string, $data.tool_input.content) | Where-Object { $_ }
    foreach ($text in $texts) {
        foreach ($line in ([string]$text -split "\r?\n")) {
            if ($line -match '^\s*//(?!/)') {
                [Console]::Error.WriteLine("Blocked: '//' comments are not allowed in C# files. Use an XML doc comment (///) above the type or member, or no comment. See .claude/rules/comments.md. Offending line: $($line.Trim())")
                exit 2
            }
        }
    }
} catch {
    exit 0
}
exit 0
