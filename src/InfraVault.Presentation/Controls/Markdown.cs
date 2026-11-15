using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace InfraVault.Presentation.Controls;

/// <summary>
/// Lecteur de documentation : rend un sous-ensemble de Markdown (titres, listes, citations, liens,
/// gras/italique, code en ligne et blocs de code avec coloration syntaxique) dans un FlowDocument.
/// </summary>
public sealed partial class MarkdownViewer : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownViewer), new PropertyMetadata("", (d, _) => ((MarkdownViewer)d).Rebuild()));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    public MarkdownViewer()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsToolBarVisible = false;
        Focusable = false;
        SetResourceReference(SelectionBrushProperty, "Accent");
    }

    [GeneratedRegex(@"(`[^`]+`)|(\*\*[^*]+\*\*)|(\*[^*\s][^*]*\*)|(\[[^\]]+\]\([^)\s]+\))")]
    private static partial Regex InlinePattern();

    [GeneratedRegex(@"^(\s*)(\d+)[.)]\s+(.*)$")]
    private static partial Regex OrderedPattern();

    private void Rebuild()
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0, 0, 14, 0), LineHeight = 22, FontSize = 13.5, TextAlignment = TextAlignment.Left };
        doc.SetResourceReference(FlowDocument.FontFamilyProperty, "Font.Body");
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Text");

        var lines = (Markdown ?? "").Replace("\r\n", "\n").Split('\n');
        List? list = null;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            AddInlines(p.Inlines, string.Join(" ", paragraph));
            doc.Blocks.Add(p);
            paragraph.Clear();
        }

        void FlushList()
        {
            if (list is not null) doc.Blocks.Add(list);
            list = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();
                var language = trimmed[3..].Trim().ToLowerInvariant();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
                    code.Add(lines[i].TrimEnd('\r'));
                doc.Blocks.Add(CodeBlock(code, language));
                continue;
            }

            if (trimmed.Length == 0) { FlushParagraph(); FlushList(); continue; }

            if (trimmed.StartsWith('#'))
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                if (level <= 4 && trimmed.Length > level && trimmed[level] == ' ')
                {
                    FlushParagraph();
                    FlushList();
                    var heading = new Paragraph
                    {
                        FontSize = level switch { 1 => 24, 2 => 18, 3 => 15, _ => 13.5 },
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, doc.Blocks.Count == 0 ? 0 : level == 1 ? 18 : 14, 0, 8),
                    };
                    heading.SetResourceReference(TextElement.FontFamilyProperty, "Font.Display");
                    AddInlines(heading.Inlines, trimmed[(level + 1)..]);
                    doc.Blocks.Add(heading);
                    continue;
                }
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                FlushList();
                var rule = new Paragraph { Margin = new Thickness(0, 6, 0, 12), FontSize = 1, BorderThickness = new Thickness(0, 1, 0, 0) };
                rule.SetResourceReference(Block.BorderBrushProperty, "Line");
                doc.Blocks.Add(rule);
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                FlushParagraph();
                FlushList();
                var quote = new Paragraph { Margin = new Thickness(0, 2, 0, 10), Padding = new Thickness(12, 6, 10, 6), BorderThickness = new Thickness(3, 0, 0, 0) };
                quote.SetResourceReference(Block.BorderBrushProperty, "Accent");
                quote.SetResourceReference(Block.BackgroundProperty, "AccentSoft");
                AddInlines(quote.Inlines, trimmed.Length > 1 ? trimmed[2..] : "");
                doc.Blocks.Add(quote);
                continue;
            }

            var ordered = OrderedPattern().Match(line);
            var bullet = trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal);
            if (bullet || ordered.Success)
            {
                FlushParagraph();
                var style = ordered.Success ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
                if (list is null || list.MarkerStyle != style)
                {
                    FlushList();
                    list = new List { MarkerStyle = style, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(22, 0, 0, 0), MarkerOffset = 8 };
                }
                var item = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                AddInlines(item.Inlines, ordered.Success ? ordered.Groups[3].Value : trimmed[2..]);
                list.ListItems.Add(new ListItem(item));
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }
        FlushParagraph();
        FlushList();
        Document = doc;
    }

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var position = 0;
        foreach (Match m in InlinePattern().Matches(text))
        {
            if (m.Index > position) inlines.Add(new Run(text[position..m.Index]));
            var token = m.Value;
            if (token.StartsWith('`'))
            {
                var run = new Run(token[1..^1]) { FontSize = 12.5 };
                run.SetResourceReference(TextElement.FontFamilyProperty, "Font.Mono");
                run.SetResourceReference(TextElement.BackgroundProperty, "CodeBg");
                run.SetResourceReference(TextElement.ForegroundProperty, "Syn.Key");
                inlines.Add(run);
            }
            else if (token.StartsWith("**", StringComparison.Ordinal))
            {
                inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith('*'))
            {
                inlines.Add(new Italic(new Run(token[1..^1])));
            }
            else
            {
                var split = token.IndexOf("](", StringComparison.Ordinal);
                var label = token[1..split];
                var url = token[(split + 2)..^1];
                var link = new Hyperlink(new Run(label)) { ToolTip = url, Cursor = Cursors.Hand };
                link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    link.NavigateUri = uri;
                    link.RequestNavigate += (_, e) =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                        e.Handled = true;
                    };
                }
                inlines.Add(link);
            }
            position = m.Index + m.Length;
        }
        if (position < text.Length) inlines.Add(new Run(text[position..]));
    }

    private static Block CodeBlock(List<string> lines, string language)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 12), Padding = new Thickness(14, 10, 14, 10), FontSize = 12.5, LineHeight = 19,
            BorderThickness = new Thickness(1), TextAlignment = TextAlignment.Left,
        };
        p.SetResourceReference(TextElement.FontFamilyProperty, "Font.Mono");
        p.SetResourceReference(Block.BackgroundProperty, "CodeBg");
        p.SetResourceReference(Block.BorderBrushProperty, "Line");
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var (text, kind) in SyntaxHighlighter.Tokenize(lines[i], language))
            {
                var run = new Run(text);
                if (kind is not null) run.SetResourceReference(TextElement.ForegroundProperty, kind);
                p.Inlines.Add(run);
            }
            if (i < lines.Count - 1) p.Inlines.Add(new LineBreak());
        }
        return p;
    }
}

/// <summary>Coloration syntaxique légère (bash, SQL, YAML, JSON, INI/nginx, PowerShell) : commentaires, chaînes, nombres, mots-clés.</summary>
public static partial class SyntaxHighlighter
{
    private static readonly HashSet<string> Shell = new(StringComparer.Ordinal)
    {
        "sudo", "if", "then", "else", "elif", "fi", "for", "do", "done", "while", "case", "esac", "function", "return", "exit", "export", "echo",
        "cd", "systemctl", "apt", "dnf", "docker", "kubectl", "ssh", "scp", "tar", "mv", "cp", "rm", "mkdir", "chmod", "chown", "grep", "curl",
        "certbot", "git", "psql", "pg_dump", "pg_restore", "nginx", "service", "journalctl", "cat", "ls", "set",
    };

    private static readonly HashSet<string> Sql = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "and", "or", "not", "insert", "into", "values", "update", "set", "delete", "create", "alter", "drop", "table",
        "index", "database", "role", "user", "grant", "revoke", "on", "to", "as", "join", "left", "right", "inner", "outer", "group", "by",
        "order", "limit", "having", "with", "null", "is", "in", "exists", "begin", "commit", "rollback", "primary", "key", "references", "owner",
    };

    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "server", "location", "listen", "proxy_pass", "upstream", "include", "root", "image", "services", "volumes",
        "ports", "networks", "environment", "restart", "function", "param", "foreach", "return", "var", "const", "let", "def", "class", "import",
    };

    [GeneratedRegex("""("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')|(\b\d+(?:\.\d+)*\b)|([A-Za-z_][A-Za-z0-9_\-]*)""")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^(\s*-?\s*)([A-Za-z0-9_.\-""]+)(\s*:)(\s|$)")]
    private static partial Regex YamlKeyPattern();

    public static IEnumerable<(string Text, string? Brush)> Tokenize(string line, string language)
    {
        var sql = language is "sql" or "psql" or "mysql";
        var comment = sql ? "--" : language is "js" or "json" or "c" or "cs" or "java" or "go" ? "//" : "#";
        string? tail = null;
        var commentAt = FindComment(line, comment);
        if (commentAt >= 0)
        {
            tail = line[commentAt..];
            line = line[..commentAt];
        }

        var start = 0;
        if (language is "yaml" or "yml" or "json" or "toml" or "ini" or "")
        {
            var key = YamlKeyPattern().Match(line);
            if (key.Success)
            {
                yield return (key.Groups[1].Value, null);
                yield return (key.Groups[2].Value, "Syn.Key");
                yield return (key.Groups[3].Value, null);
                start = key.Groups[4].Index;
            }
        }

        var keywords = sql ? Sql : language is "bash" or "sh" or "shell" or "zsh" or "console" ? Shell : Generic;
        var position = start;
        foreach (Match m in TokenPattern().Matches(line, start))
        {
            if (m.Index > position) yield return (line[position..m.Index], null);
            string? brush = m.Groups[1].Success ? "Syn.String" : m.Groups[2].Success ? "Syn.Number" : keywords.Contains(m.Value) ? "Syn.Keyword" : null;
            yield return (m.Value, brush);
            position = m.Index + m.Length;
        }
        if (position < line.Length) yield return (line[position..], null);
        if (tail is not null) yield return (tail, "Syn.Comment");
    }

    /// <summary>Position du premier marqueur de commentaire situé hors d'une chaîne.</summary>
    private static int FindComment(string line, string marker)
    {
        char quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (string.CompareOrdinal(line, i, marker, 0, marker.Length) == 0 && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return i;
        }
        return -1;
    }
}

/// <summary>Comportements de liste et d'arborescence utilisables depuis des DataTemplates (sans code-behind).</summary>
public static class ItemClick
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(ItemClick), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is not null || d is not ItemsControl control) return;
        control.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (FindItem(args.OriginalSource as DependencyObject) is { } item) Execute(control, item.DataContext);
        };
        control.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            var data = control switch
            {
                System.Windows.Controls.Primitives.Selector s => s.SelectedItem,
                TreeView t => t.SelectedItem,
                _ => null,
            };
            if (data is not null) { Execute(control, data); args.Handled = true; }
        };
    }

    private static void Execute(ItemsControl control, object? data)
    {
        var command = GetCommand(control);
        if (data is not null && command?.CanExecute(data) == true) command.Execute(data);
    }

    /// <summary>Remonte jusqu'au conteneur d'élément, en ignorant les clics sur un bouton ou un expandeur.</summary>
    private static FrameworkElement? FindItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return null;
            if (source is ListBoxItem or TreeViewItem) return (FrameworkElement)source;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
}
