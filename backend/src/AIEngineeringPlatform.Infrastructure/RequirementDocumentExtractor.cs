using AIEngineeringPlatform.Application.Estimation;
using System.IO.Compression;
using System.Text;
using System.Xml;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class RequirementDocumentExtractor : IRequirementDocumentExtractor
{
    public Task<ExtractedRequirementDocument> ExtractAsync(string fileName, byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (bytes.Length is 0 or > RequirementIntake.MaximumUploadBytes) throw new ArgumentException("Document exceeds the upload bound.");
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return Task.FromResult(extension switch
            {
                ".md" or ".txt" => Text(bytes, extension, ct),
                ".docx" => Docx(bytes, ct),
                _ => throw new ArgumentException("Supported formats: UTF-8 .md, .txt and macro-free .docx. PDF, scanned documents and legacy Word are not supported.")
            });
        }
        catch (Exception e) when (e is InvalidDataException or DecoderFallbackException or XmlException or NotSupportedException)
        { throw new ArgumentException("Document is malformed, encrypted or contains unsupported content."); }
    }
    private static ExtractedRequirementDocument Text(byte[] bytes, string extension, CancellationToken ct)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        ValidateText(text);
        var lines = text.Split('\n');
        if (lines.Length > 20000) throw new ArgumentException("Document has too many lines.");
        var fragments = new List<DocumentFragment>();
        for (var i = 0; i < lines.Length; i++) { ct.ThrowIfCancellationRequested(); fragments.Add(new("line " + (i + 1), lines[i])); }
        return new(extension == ".md" ? "text/markdown" : "text/plain", text, fragments.ToArray());
    }
    private static ExtractedRequirementDocument Docx(byte[] bytes, CancellationToken ct)
    {
        if (!bytes.AsSpan().StartsWith("PK\x03\x04"u8)) throw new ArgumentException("DOCX must be a valid OOXML ZIP document.");
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (archive.Entries.Count > 500 || archive.Entries.Select(x => x.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count) throw new ArgumentException("DOCX has excessive or duplicate entries.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            total = checked(total + entry.Length);
            if (total > 8_000_000 || entry.Length > 2_000_000 || entry.FullName.Contains("..", StringComparison.Ordinal) || entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("embeddings/", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("activeX", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("DOCX contains active, embedded or excessive content.");
        }
        var contentTypes = archive.GetEntry("[Content_Types].xml") ?? throw new ArgumentException("DOCX content types are missing.");
        var hasMainType = false;
        using (var types = Reader(contentTypes))
            while (types.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (types.NodeType == XmlNodeType.Element && types.LocalName == "Override" && types.GetAttribute("PartName") == "/word/document.xml")
                {
                    hasMainType = true;
                    if (types.GetAttribute("ContentType") != "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml") throw new ArgumentException("Macro-enabled or unsupported Word document.");
                }
            }
        if (!hasMainType) throw new ArgumentException("DOCX main content type is missing.");
        var document = archive.GetEntry("word/document.xml") ?? throw new ArgumentException("DOCX main document is missing.");
        var fragments = new List<DocumentFragment>(); var body = new StringBuilder(); var paragraph = new StringBuilder(); var index = 0; var inText = false;
        using var xml = Reader(document);
        while (xml.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (xml.NamespaceURI == "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "t") inText = !xml.IsEmptyElement;
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "t") inText = false;
                else if (xml.NodeType == XmlNodeType.Element && xml.LocalName is "br" or "tab") paragraph.Append(' ');
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "p")
                {
                    fragments.Add(new("paragraph " + ++index, paragraph.ToString())); body.AppendLine(paragraph.ToString()); paragraph.Clear();
                    if (fragments.Count > 20000 || body.Length > RequirementIntake.MaximumTextCharacters) throw new ArgumentException("DOCX extracted content exceeds bounds.");
                }
            }
            if (xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace && inText) paragraph.Append(xml.Value);
            if (paragraph.Length > RequirementIntake.MaximumTextCharacters) throw new ArgumentException("DOCX paragraph exceeds bounds.");
        }
        ValidateText(body.ToString());
        return new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", body.ToString(), fragments.ToArray());
    }
    private static XmlReader Reader(ZipArchiveEntry entry) => XmlReader.Create(new BoundedReadStream(entry.Open(), 2_000_000), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000, MaxCharactersFromEntities = 0, CloseInput = true });
    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > RequirementIntake.MaximumTextCharacters || text.Any(x => char.IsControl(x) && x is not ('\n' or '\r' or '\t'))) throw new ArgumentException("Document must contain bounded, valid text without control characters.");
    }
    private sealed class BoundedReadStream(Stream inner, long limit) : Stream
    {
        private long read;
        public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, (int)Math.Min(count, Math.Max(1, limit - read + 1))); read += n; if (read > limit) throw new InvalidDataException("Decompressed entry exceeds bound."); return n; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
