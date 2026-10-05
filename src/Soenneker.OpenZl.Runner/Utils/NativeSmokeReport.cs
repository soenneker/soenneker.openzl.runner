using System.Runtime.InteropServices;
namespace Soenneker.OpenZl.Runner.Utils;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct NativeSmokeReport
{
    [FieldOffset(0)] internal int Code;
    [FieldOffset(8)] internal nuint Value;
}
