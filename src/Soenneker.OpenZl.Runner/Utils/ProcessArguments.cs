using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Soenneker.OpenZl.Runner.Utils;

internal static class ProcessArguments
{
    public static string Join(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));
    private static string Quote(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("An argument contains a null character.");
        var b = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); b.Append(c); slashes = 0;
        }
        return b.Append('\\', slashes * 2).Append('"').ToString();
    }
}
