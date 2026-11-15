using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InfraVault.Application.Services;
using InfraVault.Presentation.Controls;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.Topology;

/// <summary>Couleurs du schéma, lues dans un dictionnaire de thème (thème courant à l'écran, thème clair à l'export).</summary>
public sealed class TopologyPalette
{
    private readonly ResourceDictionary _resources;

    private TopologyPalette(ResourceDictionary resources) => _resources = resources;

    public static TopologyPalette Current()
        => new(System.Windows.Application.Current.Resources.MergedDictionaries[0]);

    public static TopologyPalette Light()
        => new(new ResourceDictionary { Source = new Uri("pack://application:,,,/InfraVault;component/Resources/Themes/Light.xaml") });

    public Color this[string key] => ((SolidColorBrush)_resources[key]).Color;
    public Color Asset(string? name) => _resources["Asset." + name] is SolidColorBrush b ? b.Color : this["Asset.slate"];
}

public sealed record TopologyRender(DrawingGroup Drawing, double Width, double Height, IReadOnlyList<(Rect Bounds, Guid AssetId)> Hits);

/// <summary>
/// Dessine la vue globale : cadres emboîtés pour l'hébergement (site › hôte › VM › stack › conteneur),
/// flèches pour les flux avec leurs ports, et nuage « Internet » relié aux ressources exposées.
/// </summary>
public static class TopologyRenderer
{
    private const double LeafWidth = 186, LeafHeight = 58, Header = 38, Pad = 14, Gap = 14, Margin = 36;
    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Mono = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private sealed class Box(TopologyNode node, int depth)
    {
        public TopologyNode Node { get; } = node;
        public int Depth { get; } = depth;
        public List<Box> Children { get; } = [];
        public double X, Y, W, H;
        public Rect Bounds => new(X, Y, W, H);
        public bool IsLeaf => Children.Count == 0;
    }

    public static TopologyRender Render(TopologyModel model, TopologyPalette palette, bool showPorts = true, bool withTitle = false)
    {
        var roots = model.Roots.Select(r => Measure(r, 0)).ToList();
        var hasInternet = model.Exposures.Count > 0;
        var top = Margin + (withTitle ? 62 : 0) + (hasInternet ? 104 : 0);

        // Disposition des racines en lignes.
        var limit = Math.Max(roots.Count == 0 ? 600 : roots.Max(r => r.W), Math.Sqrt(roots.Sum(r => r.W * r.H)) * 1.7);
        double x = Margin, y = top, rowHeight = 0, width = 600;
        foreach (var root in roots)
        {
            if (x > Margin && x + root.W > Margin + limit) { x = Margin; y += rowHeight + Gap * 2; rowHeight = 0; }
            Place(root, x, y);
            x += root.W + Gap * 2;
            rowHeight = Math.Max(rowHeight, root.H);
            width = Math.Max(width, x - Gap * 2 + Margin);
        }
        var height = y + rowHeight + Margin + 34;

        var all = new Dictionary<Guid, Box>();
        void Index(Box b) { all[b.Node.Id] = b; foreach (var c in b.Children) Index(c); }
        roots.ForEach(Index);

        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brush(palette["Bg"]), null, new Rect(0, 0, width, height));

            if (withTitle)
            {
                dc.DrawText(Text(model.Title, 22, palette["Text"], Bold), new Point(Margin, Margin - 6));
                dc.DrawText(Text($"Coffre « {model.VaultName} » · {model.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}", 12, palette["TextMuted"], Regular),
                    new Point(Margin, Margin + 26));
            }

            foreach (var root in roots) DrawBox(dc, root, palette);

            // Flux entre ressources.
            var labels = new List<(Point At, string Text, Color Color)>();
            var seen = new Dictionary<(Guid, Guid), int>();
            foreach (var flow in model.Flows)
            {
                if (!all.TryGetValue(flow.SourceId, out var source) || !all.TryGetValue(flow.TargetId, out var target)) continue;
                var pair = flow.SourceId.CompareTo(flow.TargetId) < 0 ? (flow.SourceId, flow.TargetId) : (flow.TargetId, flow.SourceId);
                var n = seen[pair] = seen.GetValueOrDefault(pair) + 1;
                var color = FlowColor(flow.Type, palette);
                var label = flow.Ports.Count > 0 ? string.Join(", ", flow.Ports) : flow.Label;
                var mid = DrawArrow(dc, Anchor(source), Anchor(target), source.Bounds, target.Bounds, color, 18 + (n - 1) * 22, dashed: flow.Ports.Count == 0);
                // Seuls les flux portant des ports sont étiquetés : les relations simples restent lisibles sans texte.
                if (showPorts && flow.Ports.Count > 0) labels.Add((mid, label, color));
            }

            // Internet → ressources exposées.
            if (hasInternet)
            {
                var cloud = new Rect(width / 2 - 92, Margin + (withTitle ? 62 : 0), 184, 50);
                var danger = palette["Danger"];
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(34, danger.R, danger.G, danger.B)), new Pen(Brush(danger), 1.6), cloud, 25, 25);
                DrawIcon(dc, "globe", new Point(cloud.X + 22, cloud.Y + 14), 22, danger);
                dc.DrawText(Text("Internet", 15, palette["Text"], Bold), new Point(cloud.X + 54, cloud.Y + 14));
                foreach (var exposure in model.Exposures)
                {
                    if (!all.TryGetValue(exposure.AssetId, out var box)) continue;
                    var from = new Point(cloud.X + cloud.Width / 2, cloud.Bottom);
                    var mid = DrawArrow(dc, from, new Point(box.X + box.W / 2, box.Y + 8), cloud, box.Bounds, danger, 0, dashed: false, thickness: 2);
                    if (showPorts)
                        labels.Add((mid, exposure.Ports.Count > 0 ? string.Join(", ", exposure.Ports) : "exposé", danger));
                }
            }

            // Étiquettes de ports par-dessus les flèches.
            var placed = new List<Rect>();
            foreach (var (at, text, color) in labels)
            {
                var ft = Text(text, 10.5, palette["Text"], Mono);
                var rect = new Rect(at.X - ft.Width / 2 - 6, at.Y - ft.Height / 2 - 2.5, ft.Width + 12, ft.Height + 5);
                // Évite la superposition de deux étiquettes en décalant la seconde.
                for (var attempt = 0; attempt < 8 && placed.Any(r => r.IntersectsWith(rect)); attempt++)
                    rect.Y += rect.Height + 3;
                placed.Add(rect);
                dc.DrawRoundedRectangle(Brush(palette["Surface"]), new Pen(Brush(color), 1), rect, 7, 7);
                dc.DrawText(ft, new Point(rect.X + 6, rect.Y + 2.5));
            }

            // Légende.
            var legendY = height - 28;
            dc.DrawText(Text("Cadres emboîtés : hébergement   ·   Flèche pleine : flux avec ports   ·   Pointillés : relation sans port   ·   Rouge : exposition Internet",
                10.5, palette["TextFaint"], Regular), new Point(Margin, legendY));
        }
        drawing.Freeze();

        return new TopologyRender(drawing, width, height,
            all.Values.OrderByDescending(b => b.Depth).Select(b => (b.Bounds, b.Node.Id)).ToList());
    }

    /// <summary>Rend le schéma en PNG (thème clair, adapté à l'impression et aux documents).</summary>
    public static (byte[] Png, int Width, int Height) RenderPng(TopologyModel model, double scale = 2)
    {
        var render = Render(model, TopologyPalette.Light(), showPorts: true, withTitle: true);
        // Borne la taille de l'image pour rester dans des dimensions raisonnables.
        scale = Math.Min(scale, Math.Min(9000 / render.Width, 9000 / render.Height));
        scale = Math.Max(scale, 0.25);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawDrawing(render.Drawing);
        int w = (int)Math.Ceiling(render.Width * scale), h = (int)Math.Ceiling(render.Height * scale);
        var bitmap = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return (stream.ToArray(), w, h);
    }

    // ───────────────────────── Mise en page ─────────────────────────

    private static Box Measure(TopologyNode node, int depth)
    {
        var box = new Box(node, depth);
        foreach (var child in node.Children) box.Children.Add(Measure(child, depth + 1));
        if (box.IsLeaf)
        {
            box.W = LeafWidth;
            box.H = LeafHeight + (node.ListeningPorts.Count > 0 || node.InternetExposed ? 16 : 0);
            return box;
        }

        // Feuilles d'abord, puis cadres : les petits éléments restent groupés.
        var ordered = box.Children.OrderBy(c => c.IsLeaf ? 0 : 1).ToList();
        box.Children.Clear();
        box.Children.AddRange(ordered);

        var limit = Math.Max(ordered.Max(c => c.W), Math.Sqrt(ordered.Sum(c => (c.W + Gap) * (c.H + Gap))) * 1.5);
        double x = 0, y = 0, rowHeight = 0, maxWidth = 0;
        foreach (var child in ordered)
        {
            if (x > 0 && x + child.W > limit) { x = 0; y += rowHeight + Gap; rowHeight = 0; }
            child.X = x;
            child.Y = y;
            x += child.W + Gap;
            rowHeight = Math.Max(rowHeight, child.H);
            maxWidth = Math.Max(maxWidth, x - Gap);
        }
        var headerWidth = Text(node.Name, 13, Colors.Black, Bold).Width + Text(node.TypeName, 11, Colors.Black, Regular).Width + 90;
        box.W = Math.Max(maxWidth, headerWidth) + Pad * 2;
        box.H = Header + y + rowHeight + Pad;
        return box;
    }

    private static void Place(Box box, double x, double y)
    {
        box.X = x;
        box.Y = y;
        foreach (var child in box.Children) Place(child, x + Pad + child.X, y + Header + child.Y);
    }

    // ───────────────────────── Dessin ─────────────────────────

    private static void DrawBox(DrawingContext dc, Box box, TopologyPalette p)
    {
        var node = box.Node;
        var color = p.Asset(node.Color);
        var exposed = node.InternetExposed;
        var border = exposed ? p["Danger"] : box.IsLeaf ? p["LineStrong"] : Blend(color, p["Line"], 0.45);
        var retired = node.Status == AssetStatuses.Retired;

        if (box.IsLeaf)
        {
            dc.DrawRoundedRectangle(Brush(p["Surface"]), new Pen(Brush(border), exposed ? 1.8 : 1), box.Bounds, 8, 8);
            dc.DrawRoundedRectangle(Brush(color), null, new Rect(box.X, box.Y + 10, 3, box.H - 20), 1.5, 1.5);
            DrawIcon(dc, node.Icon, new Point(box.X + 13, box.Y + 12), 20, color);
            dc.DrawText(Text(node.Name, 12.5, retired ? p["TextFaint"] : p["Text"], Bold, box.W - 52), new Point(box.X + 41, box.Y + 9));
            var subtitle = node.IpAddress ?? node.Hostname ?? node.TypeName;
            dc.DrawText(Text(subtitle, 10.5, p["TextMuted"], node.IpAddress is null ? Regular : Mono, box.W - 52), new Point(box.X + 41, box.Y + 28));
            var ports = exposed && node.ExposedPorts.Count > 0 ? "public " + string.Join(" ", node.ExposedPorts)
                : node.ListeningPorts.Count > 0 ? "écoute " + string.Join(" ", node.ListeningPorts) : exposed ? "exposé sur Internet" : null;
            if (ports is not null)
                dc.DrawText(Text(ports, 10, exposed ? p["Danger"] : p["Teal"], Mono, box.W - 24), new Point(box.X + 13, box.Y + 49));
        }
        else
        {
            var fill = box.Depth % 2 == 0 ? p["SurfaceAlt"] : p["Surface"];
            dc.DrawRoundedRectangle(Brush(fill), new Pen(Brush(border), exposed ? 1.8 : 1.2), box.Bounds, 11, 11);
            DrawIcon(dc, node.Icon, new Point(box.X + 12, box.Y + 9), 20, color);
            var name = Text(node.Name, 13, p["Text"], Bold, box.W - 60);
            dc.DrawText(name, new Point(box.X + 40, box.Y + 9));
            var meta = node.TypeName + (node.IpAddress is null ? "" : " · " + node.IpAddress);
            var remaining = box.W - 60 - name.Width - 14;
            if (remaining > 40)
                dc.DrawText(Text(meta, 10.5, p["TextMuted"], Regular, remaining), new Point(box.X + 40 + name.Width + 10, box.Y + 12));
            foreach (var child in box.Children) DrawBox(dc, child, p);
        }

        if (node.Status == AssetStatuses.Maintenance)
            dc.DrawEllipse(Brush(p["Warning"]), null, new Point(box.X + box.W - 12, box.Y + 12), 4, 4);
    }

    private static void DrawIcon(DrawingContext dc, string icon, Point at, double size, Color color)
    {
        dc.PushTransform(new TranslateTransform(at.X, at.Y));
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        var pen = new Pen(Brush(color), 1.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, Icons.Get(icon));
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Point d'ancrage d'un flux : le centre pour une feuille, la barre de titre pour un cadre.</summary>
    private static Point Anchor(Box box) => box.IsLeaf
        ? new Point(box.X + box.W / 2, box.Y + box.H / 2)
        : new Point(box.X + box.W / 2, box.Y + Header / 2);

    private static Point DrawArrow(DrawingContext dc, Point from, Point to, Rect fromRect, Rect toRect, Color color, double bend,
        bool dashed, double thickness = 1.5)
    {
        var start = Clip(from, to, fromRect);
        var end = Clip(to, from, toRect);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        // Courbe légère : sépare les flux parallèles et les allers-retours.
        var control = new Point((start.X + end.X) / 2 - dy / length * bend, (start.Y + end.Y) / 2 + dx / length * bend);

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false, false);
            g.QuadraticBezierTo(control, end, true, true);
        }
        var pen = new Pen(Brush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dashed) pen.DashStyle = new DashStyle([4, 3], 0);
        dc.DrawGeometry(null, pen, geometry);

        // Pointe orientée selon la tangente d'arrivée.
        var tx = end.X - control.X;
        var ty = end.Y - control.Y;
        var tl = Math.Max(1, Math.Sqrt(tx * tx + ty * ty));
        tx /= tl; ty /= tl;
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(end, true, true);
            g.LineTo(new Point(end.X - tx * 9 - ty * 4.2, end.Y - ty * 9 + tx * 4.2), true, true);
            g.LineTo(new Point(end.X - tx * 9 + ty * 4.2, end.Y - ty * 9 - tx * 4.2), true, true);
        }
        dc.DrawGeometry(Brush(color), null, head);

        return new Point(0.25 * start.X + 0.5 * control.X + 0.25 * end.X, 0.25 * start.Y + 0.5 * control.Y + 0.25 * end.Y);
    }

    /// <summary>Intersection du segment (inside → outside) avec le bord du rectangle.</summary>
    private static Point Clip(Point inside, Point outside, Rect rect)
    {
        if (!rect.Contains(inside) || rect.Contains(outside)) return inside;
        var dx = outside.X - inside.X;
        var dy = outside.Y - inside.Y;
        var t = 1.0;
        if (dx > 0) t = Math.Min(t, (rect.Right - inside.X) / dx);
        if (dx < 0) t = Math.Min(t, (rect.Left - inside.X) / dx);
        if (dy > 0) t = Math.Min(t, (rect.Bottom - inside.Y) / dy);
        if (dy < 0) t = Math.Min(t, (rect.Top - inside.Y) / dy);
        return new Point(inside.X + dx * t, inside.Y + dy * t);
    }

    private static Color FlowColor(string type, TopologyPalette p) => type switch
    {
        RelationshipTypes.Uses => p["Teal"],
        RelationshipTypes.ConnectsTo => p.Asset("sky"),
        RelationshipTypes.BacksUpTo or RelationshipTypes.ReplicatesTo => p.Asset("amber"),
        RelationshipTypes.DependsOn => p.Asset("violet"),
        RelationshipTypes.Protects => p.Asset("lime"),
        _ => p.Asset("slate"),
    };

    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R * amount + b.R * (1 - amount)), (byte)(a.G * amount + b.G * (1 - amount)), (byte)(a.B * amount + b.B * (1 - amount)));

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static FormattedText Text(string text, double size, Color color, Typeface typeface, double maxWidth = 0)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brush(color), 1.0)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        if (maxWidth > 0) ft.MaxTextWidth = Math.Max(10, maxWidth);
        return ft;
    }
}
