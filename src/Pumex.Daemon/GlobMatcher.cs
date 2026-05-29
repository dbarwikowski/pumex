using System.Text;
using System.Text.RegularExpressions;

namespace Pumex.Daemon;

/// <summary>
/// Minimal, dependency-free glob matcher for ignore rules, matched against
/// vault-relative paths using <c>/</c> separators. Semantics:
/// <list type="bullet">
///   <item>A pattern without <c>/</c> matches the file's basename at any depth
///     (e.g. <c>*.log</c> ignores every <c>.log</c> file).</item>
///   <item>A pattern with <c>/</c> matches the full relative path, anchored
///     (e.g. <c>templates/**</c>, <c>archive/2019/*.md</c>).</item>
///   <item><c>**</c> crosses directory separators; <c>*</c> and <c>?</c> do not.</item>
/// </list>
/// Matching is case-insensitive.
/// </summary>
public sealed class GlobMatcher
{
    private readonly List<(Regex Regex, bool AgainstBasename)> _patterns = new();

    public GlobMatcher(IEnumerable<string> globs)
    {
        foreach (var glob in globs)
        {
            if (string.IsNullOrWhiteSpace(glob)) continue;
            var normalized = glob.Replace('\\', '/').Trim();
            var againstBasename = !normalized.Contains('/');
            _patterns.Add((new Regex(ToRegex(normalized), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), againstBasename));
        }
    }

    public bool IsEmpty => _patterns.Count == 0;

    public bool IsMatch(string relativePath)
    {
        if (_patterns.Count == 0) return false;
        var rel = relativePath.Replace('\\', '/');
        var basename = rel[(rel.LastIndexOf('/') + 1)..];
        foreach (var (regex, againstBasename) in _patterns)
            if (regex.IsMatch(againstBasename ? basename : rel))
                return true;
        return false;
    }

    private static string ToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
