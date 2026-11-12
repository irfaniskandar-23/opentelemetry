---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.http"
---

# Comments

Applies to C#, `.csproj` (`<!-- -->`) and `.http` (`#`) files. Same purpose and length rules in each, using that file's own syntax.

## Rules

- C#: comments are XML doc comments (`///`) on types and members only. No `//` comments anywhere, not inside method bodies, not in `Program.cs`. C# only allows XML doc comments directly above a type or member; elsewhere the compiler warns CS1587.
- A comment says what the code is trying to achieve, never what it does line by line.
- Only comment what is not obvious.
- When code follows a known practice, link the docs page with `<see href="..."/>` instead of explaining it. Never quote the docs.
- Under 20 words per comment.
- Every `Add...`/`Use...` extension method gets a one-line `<summary>` stating its purpose (it is what hovering the call in `Program.cs` shows). Other members get one only when something is not obvious.
- A hook blocks whole-line `//` comments in `.cs` edits. Trailing comments (`x(); // ...`) are not caught by the hook but are still forbidden.
- The rules apply however a file is changed; the hook only sees Edit/Write, not shell edits.

## Example

Good:

```csharp
/// <summary>Registers the named HttpClient that calls the Nominatim geocoding API.</summary>
public static IServiceCollection AddGeocoding(this IServiceCollection services)
```

Bad:

```csharp
// loop over the stores and add each one to the list
foreach (var store in stores) list.Add(store);
```

## Links

- <https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/documentation-comments>
- <https://learn.microsoft.com/dotnet/csharp/misc/cs1587>
- <https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/recommended-tags#generate-links-and-references>
