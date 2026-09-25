using System;

namespace LifeLink.Common
{
    /// <summary>
    /// Uploaded files travel and are stored as data URLs ("data:&lt;type&gt;;base64,&lt;payload&gt;"). A file with an empty
    /// payload counts as "no file": it is refused on upload and never offered for download.
    /// </summary>
    public static class AttachmentRules
    {
        /// <summary>About 2 MB of file once base64-encoded; the same limit as complaint and appeal attachments.</summary>
        public const int MaxDataUrlLength = 2_800_000;

        public const string EmptyFileMessage = "The selected file is empty. Please choose a file that has content.";

        public static bool HasContent(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;

            var comma = url.IndexOf(',');
            if (comma < 0) return false;
            for (var i = comma + 1; i < url.Length; i++)
            {
                if (!char.IsWhiteSpace(url[i])) return true;
            }
            return false;
        }

        /// <summary>Blank means "no file"; anything else must be a data URL with content.</summary>
        public static void EnsureValidIfPresent(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid attachment.");
            }
            if (!HasContent(url))
            {
                throw new InvalidOperationException(EmptyFileMessage);
            }
        }
    }
}
