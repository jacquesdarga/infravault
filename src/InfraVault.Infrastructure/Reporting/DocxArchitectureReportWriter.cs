using System.Globalization;
using System.IO.Compression;
using System.Text;
using InfraVault.Application.Services;
using InfraVault.Shared.Constants;

namespace InfraVault.Infrastructure.Reporting;

/// <summary>
/// Génère le dossier d'architecture au format Word (.docx, Office Open XML) sans dépendance externe :
/// schéma global, exposition Internet, matrice des flux et des ports, détail des stacks, inventaire.
/// Le document ne contient jamais de secret ni de propriété sensible.
/// </summary>
public sealed class DocxArchitectureReportWriter : IArchitectureReportWriter
{
    private const string Accent = "B4531F";
    private const string Ink = "1F2A2A";
    private const string Muted = "6B7574";
    private const string HeaderFill = "243332";
    private const string ZebraFill = "F4EFE6";
    private const int PageWidthTwips = 16838, PageHeightTwips = 11906, MarginTwips = 1000;
    private const long EmuPerTwip = 635, EmuPerPixel = 9525;

    public byte[] Write(TopologyModel model, byte[]? diagramPng, int diagramWidthPx, int diagramHeightPx)
    {
        var body = new StringBuilder();
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var nodes = model.AllNodes().ToList();

        body.Append(Paragraph(model.Title, "Title"));
        body.Append(Paragraph($"Coffre « {model.VaultName} » · généré le {model.GeneratedAtUtc.ToLocalTime().ToString("d MMMM yyyy 'à' HH:mm", culture)} · InfraVault {AppInfo.Version}", "Subtitle"));
        body.Append(Paragraph(
            $"{nodes.Count} ressources · {model.Flows.Count} flux · {model.Exposures.Count} point(s) d'entrée Internet · {model.Stacks.Count} stack(s) de conteneurs.",
            "Lead"));

        // 1. Schéma
        body.Append(Paragraph("1. Vue d'ensemble", "Heading1"));
        if (diagramPng is { Length: > 0 } && diagramWidthPx > 0 && diagramHeightPx > 0)
        {
            body.Append(Image(diagramWidthPx, diagramHeightPx));
            body.Append(Paragraph("Les cadres emboîtés indiquent l'hébergement (site › hôte › VM › stack › conteneur). Les flèches portent les ports utilisés ; le nuage représente Internet.", "Caption"));
        }
        else
        {
            body.Append(Paragraph("Schéma non inclus dans cet export.", "Caption"));
        }

        // 2. Exposition Internet
        body.Append(Paragraph("2. Exposition Internet", "Heading1"));
        if (model.Exposures.Count == 0)
        {
            body.Append(Paragraph("Aucune ressource n'est déclarée comme exposée sur Internet.", "Normal"));
        }
        else
        {
            body.Append(Paragraph("Ressources joignables depuis l'extérieur et ports ouverts.", "Normal"));
            body.Append(Table(["Ressource", "Type", "Adresse publique", "Ports ouverts", "Emplacement"], [2600, 2000, 3200, 3200, 3838],
                model.Exposures.Select(e => new[] { e.AssetName, e.TypeName, e.PublicAddress ?? "—", Ports(e.Ports), e.Location })));
        }

        // 3. Flux
        body.Append(Paragraph("3. Flux et ports", "Heading1"));
        if (model.Flows.Count == 0)
        {
            body.Append(Paragraph("Aucun flux n'est documenté.", "Normal"));
        }
        else
        {
            body.Append(Paragraph("Chaque ligne décrit une communication de la source vers la cible.", "Normal"));
            body.Append(Table(["Source", "Relation", "Cible", "Ports", "Description"], [3000, 1900, 3000, 2400, 4538],
                model.Flows.Select(f => new[] { f.SourceName, f.Label, f.TargetName, Ports(f.Ports), f.Description ?? "" })));
        }

        // 4. Stacks
        body.Append(Paragraph("4. Stacks et conteneurs", "Heading1"));
        if (model.Stacks.Count == 0)
        {
            body.Append(Paragraph("Aucune stack de conteneurs n'est enregistrée.", "Normal"));
        }
        foreach (var stack in model.Stacks)
        {
            body.Append(Paragraph(stack.Stack.Name, "Heading2"));
            var facts = new List<string> { $"Emplacement : {stack.Location}" };
            foreach (var (key, label) in new[] { ("orchestrator", "Orchestration"), ("compose_path", "Fichier"), ("network", "Réseau interne") })
                if (stack.Stack.Properties.TryGetValue(key, out var value)) facts.Add($"{label} : {value}");
            body.Append(Paragraph(string.Join("   ·   ", facts), "Normal"));

            if (stack.Members.Count > 0)
            {
                body.Append(Paragraph("Conteneurs", "Heading3"));
                body.Append(Table(["Conteneur", "Image", "Ports publiés (hôte:conteneur)", "Ports internes", "Réseaux", "Internet"],
                    [2400, 4000, 2700, 2200, 2038, 1500],
                    stack.Members.Select(m => new[]
                    {
                        m.Name,
                        m.Properties.TryGetValue("image", out var image)
                            ? image + (m.Properties.TryGetValue("image_tag", out var tag) ? ":" + tag : "") : "—",
                        m.Properties.GetValueOrDefault("ports", "—"),
                        m.ListeningPorts.Count > 0 ? Ports(m.ListeningPorts) : "—",
                        m.Properties.GetValueOrDefault("networks", "—"),
                        m.InternetExposed ? Ports(m.ExposedPorts) : "non",
                    })));
            }
            if (stack.InternalFlows.Count > 0)
            {
                body.Append(Paragraph("Communications internes à la stack", "Heading3"));
                body.Append(Table(["De", "Vers", "Ports", "Description"], [3200, 3200, 2600, 5838],
                    stack.InternalFlows.Select(f => new[] { f.SourceName, f.TargetName, Ports(f.Ports), f.Description ?? "" })));
            }
            if (stack.ExternalFlows.Count > 0)
            {
                body.Append(Paragraph("Échanges avec l'extérieur de la stack", "Heading3"));
                body.Append(Table(["De", "Vers", "Ports", "Description"], [3200, 3200, 2600, 5838],
                    stack.ExternalFlows.Select(f => new[] { f.SourceName, f.TargetName, Ports(f.Ports), f.Description ?? "" })));
            }
        }

        // 5. Inventaire
        body.Append(Paragraph("5. Inventaire", "Heading1"));
        var rows = new List<string[]>();
        void Walk(TopologyNode n, int depth)
        {
            rows.Add([new string(' ', depth * 3) + (depth > 0 ? "└ " : "") + n.Name, n.TypeName, n.Hostname ?? "", n.IpAddress ?? "", n.Environment, StatusLabel(n.Status)]);
            foreach (var child in n.Children) Walk(child, depth + 1);
        }
        foreach (var root in model.Roots) Walk(root, 0);
        body.Append(Table(["Ressource", "Type", "Hostname", "Adresse IP", "Environnement", "Statut"], [4800, 2500, 2500, 2000, 1700, 1338], rows));

        body.Append($"<w:sectPr><w:pgSz w:w=\"{PageWidthTwips}\" w:h=\"{PageHeightTwips}\" w:orient=\"landscape\"/>" +
                    $"<w:pgMar w:top=\"{MarginTwips}\" w:right=\"{MarginTwips}\" w:bottom=\"{MarginTwips}\" w:left=\"{MarginTwips}\" w:header=\"500\" w:footer=\"500\" w:gutter=\"0\"/></w:sectPr>");

        var document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
            "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            $"<w:body>{body}</w:body></w:document>";

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var hasImage = diagramPng is { Length: > 0 };
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "</Relationships>");
            Add(zip, "docProps/core.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dc:title>{Esc(model.Title)}</dc:title><dc:creator>InfraVault</dc:creator>" +
                $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{model.GeneratedAtUtc:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>" +
                "</cp:coreProperties>");
            Add(zip, "word/_rels/document.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                (hasImage ? "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/diagram.png\"/>" : "") +
                "</Relationships>");
            Add(zip, "word/styles.xml", Styles());
            Add(zip, "word/document.xml", document);
            if (hasImage)
            {
                var entry = zip.CreateEntry("word/media/diagram.png", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(diagramPng!);
            }
        }
        return output.ToArray();
    }

    private static string Ports(IReadOnlyList<string> ports) => ports.Count == 0 ? "—" : string.Join(", ", ports);

    private static string StatusLabel(string status) => status switch
    {
        AssetStatuses.Active => "Actif",
        AssetStatuses.Maintenance => "Maintenance",
        AssetStatuses.Retired => "Retiré",
        _ => "Inconnu",
    };

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Paragraph(string text, string style)
        => $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr><w:r><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p>";

    private static string Table(string[] headers, int[] widths, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLayout w:type=\"fixed\"/>" +
                  "<w:tblBorders>" +
                  "<w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"D8D0C0\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"D8D0C0\"/>" +
                  "<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"E6DFD1\"/>" +
                  "</w:tblBorders>" +
                  "<w:tblCellMar><w:top w:w=\"70\" w:type=\"dxa\"/><w:left w:w=\"110\" w:type=\"dxa\"/><w:bottom w:w=\"70\" w:type=\"dxa\"/><w:right w:w=\"110\" w:type=\"dxa\"/></w:tblCellMar>" +
                  "</w:tblPr><w:tblGrid>");
        foreach (var w in widths) sb.Append($"<w:gridCol w:w=\"{w}\"/>");
        sb.Append("</w:tblGrid>");

        sb.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
        for (var i = 0; i < headers.Length; i++)
        {
            sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{widths[i]}\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{HeaderFill}\"/></w:tcPr>" +
                      $"<w:p><w:pPr><w:pStyle w:val=\"Cell\"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val=\"FFFFFF\"/></w:rPr><w:t xml:space=\"preserve\">{Esc(headers[i])}</w:t></w:r></w:p></w:tc>");
        }
        sb.Append("</w:tr>");

        var index = 0;
        foreach (var row in rows)
        {
            var fill = index++ % 2 == 1 ? $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{ZebraFill}\"/>" : "";
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
            for (var i = 0; i < headers.Length; i++)
            {
                var text = i < row.Length ? row[i] : "";
                sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{widths[i]}\" w:type=\"dxa\"/>{fill}</w:tcPr>" +
                          $"<w:p><w:pPr><w:pStyle w:val=\"Cell\"/></w:pPr><w:r>{(i == 0 ? "<w:rPr><w:b/></w:rPr>" : "")}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p></w:tc>");
            }
            sb.Append("</w:tr>");
        }
        sb.Append("</w:tbl><w:p><w:pPr><w:pStyle w:val=\"Cell\"/></w:pPr></w:p>");
        return sb.ToString();
    }

    private static string Image(int widthPx, int heightPx)
    {
        // Ajuste le schéma à la zone utile de la page paysage en conservant ses proportions.
        var maxWidth = (PageWidthTwips - 2 * MarginTwips) * EmuPerTwip;
        var maxHeight = (long)((PageHeightTwips - 2 * MarginTwips) * EmuPerTwip * 0.72);
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / (widthPx * EmuPerPixel), (double)maxHeight / (heightPx * EmuPerPixel)));
        var cx = (long)(widthPx * EmuPerPixel * scale);
        var cy = (long)(heightPx * EmuPerPixel * scale);
        return "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>" +
               $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/>" +
               "<wp:docPr id=\"1\" name=\"Schéma d'infrastructure\" descr=\"Vue globale de l'infrastructure\"/>" +
               "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
               "<pic:pic><pic:nvPicPr><pic:cNvPr id=\"1\" name=\"diagram.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
               "<pic:blipFill><a:blip r:embed=\"rId2\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
               $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
               "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    private static string Styles()
    {
        static string Style(string id, string name, string paragraph, string run, string? basedOn = "Normal") =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/>" +
            (basedOn is null ? "" : $"<w:basedOn w:val=\"{basedOn}\"/>") +
            $"<w:pPr>{paragraph}</w:pPr><w:rPr>{run}</w:rPr></w:style>";

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
               "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\" w:cs=\"Segoe UI\"/>" +
               $"<w:color w:val=\"{Ink}\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/><w:lang w:val=\"fr-FR\"/></w:rPr></w:rPrDefault>" +
               "<w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"264\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
               "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
               Style("Title", "Title", "<w:spacing w:after=\"60\"/>", $"<w:b/><w:color w:val=\"{Ink}\"/><w:sz w:val=\"52\"/><w:szCs w:val=\"52\"/>") +
               Style("Subtitle", "Subtitle", "<w:spacing w:after=\"200\"/>", $"<w:color w:val=\"{Muted}\"/><w:sz w:val=\"20\"/>") +
               Style("Lead", "Lead", $"<w:pBdr><w:left w:val=\"single\" w:sz=\"24\" w:space=\"8\" w:color=\"{Accent}\"/></w:pBdr><w:spacing w:after=\"240\"/>", "<w:sz w:val=\"22\"/>") +
               Style("Heading1", "heading 1", "<w:keepNext/><w:spacing w:before=\"360\" w:after=\"140\"/><w:outlineLvl w:val=\"0\"/>", $"<w:b/><w:color w:val=\"{Accent}\"/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/>") +
               Style("Heading2", "heading 2", "<w:keepNext/><w:spacing w:before=\"280\" w:after=\"100\"/><w:outlineLvl w:val=\"1\"/>", $"<w:b/><w:color w:val=\"{Ink}\"/><w:sz w:val=\"26\"/><w:szCs w:val=\"26\"/>") +
               Style("Heading3", "heading 3", "<w:keepNext/><w:spacing w:before=\"200\" w:after=\"80\"/><w:outlineLvl w:val=\"2\"/>", $"<w:b/><w:color w:val=\"{Muted}\"/><w:sz w:val=\"20\"/><w:caps/>") +
               Style("Caption", "caption", "<w:jc w:val=\"center\"/><w:spacing w:before=\"80\" w:after=\"200\"/>", $"<w:i/><w:color w:val=\"{Muted}\"/><w:sz w:val=\"18\"/>") +
               Style("Cell", "Cell", "<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>", "<w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/>") +
               "</w:styles>";
    }

    /// <summary>Échappe le texte pour XML et retire les caractères interdits par XML 1.0.</summary>
    private static string Esc(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\n': sb.Append(' '); break;
                default:
                    if (c >= 0x20 || c == '\t') sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
