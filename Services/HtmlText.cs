using System;
using System.Net;
using System.Text.RegularExpressions;

namespace PipeWizard.Services;

/// <summary>GameBanana descriptions are HTML or BBCode depending on how they were written.</summary>
public static partial class HtmlText
{
    public static string ToPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text;
        result = Regex.Replace(result, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<\s*/\s*(p|div|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<\s*li[^>]*>", "• ", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<[^>]+>", string.Empty);
        result = WebUtility.HtmlDecode(result);

        // BBCode, for descriptions saved before GameBanana switched editors.
        result = Regex.Replace(result, @"\[url=([^\]]+)\](.*?)\[/url\]", "$2 ($1)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"\[img\](.*?)\[/img\]", "$1",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"\[/?(b|i|u|s|size|color|font|center|left|right|quote|code|spoiler)[^\]]*\]",
            string.Empty, RegexOptions.IgnoreCase);

        result = result.Replace("\r\n", "\n", StringComparison.Ordinal);
        result = Regex.Replace(result, @"[ \t]+\n", "\n");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result.Trim();
    }
}
