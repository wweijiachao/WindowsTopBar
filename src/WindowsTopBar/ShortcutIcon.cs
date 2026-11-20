using System.IO;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace WindowsTopBar;

internal static class ShortcutIcon
{
    public static ShortcutEntry Load(ShortcutEntry entry)
    {
        try
        {
            var path = Path.ChangeExtension(entry.Path, ".icon.xaml");
            if (!File.Exists(path)) return entry;
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 65536
            });
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "Path" || root.HasElements ||
                (root.Name.NamespaceName != "" && root.Name.NamespaceName != "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
                return entry;
            var data = ((string?)root.Attribute("Data"))?.Trim();
            if (string.IsNullOrEmpty(data)) return entry;
            // Parse geometry only, never instantiate external XAML objects or evaluate markup extensions.
            var geometry = Geometry.Parse(data);
            if (geometry.Bounds.IsEmpty || geometry.Bounds.Width <= 0 || geometry.Bounds.Height <= 0) return entry;
            geometry.Freeze();
            return entry with { IconData = data, IconGeometry = geometry };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or
            FormatException or ArgumentException or InvalidOperationException)
        {
            return entry;
        }
    }
}
