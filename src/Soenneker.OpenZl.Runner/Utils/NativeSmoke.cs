using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Soenneker.OpenZl.Runner.Utils;

internal static unsafe class NativeSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Create();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Free(nint context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSmokeReport SetParameter(nint context, int parameter, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSmokeReport Select(nint context, uint graph);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSmokeReport Reference(nint context, nint compressor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSmokeReport Codec(nint context, byte* dst, nuint capacity, byte* src, nuint size);

    internal static void Verify(string path)
    {
        nint library = NativeLibrary.Load(path);
        try
        {
            T Load<T>(string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

            Free freeCompressor = Load<Free>("ZL_Compressor_free");
            Free freeCctx = Load<Free>("ZL_CCtx_free");
            Free freeDctx = Load<Free>("ZL_DCtx_free");
            nint compressor = 0, cctx = 0, dctx = 0;
            try
            {
                compressor = Load<Create>("ZL_Compressor_create")();
                cctx = Load<Create>("ZL_CCtx_create")();
                dctx = Load<Create>("ZL_DCtx_create")();
                if (compressor == 0 || cctx == 0 || dctx == 0)
                    throw new OutOfMemoryException();
                Check(Load<Select>("ZL_Compressor_selectStartingGraphID")(compressor, 13));
                Check(Load<Reference>("ZL_CCtx_refCompressor")(cctx, compressor));
                Check(Load<SetParameter>("ZL_CCtx_setParameter")(cctx, 4, 27));
                byte[] input = Enumerable.Range(0, 65536).Select(i => (byte)(i % 251)).ToArray();
                byte[] encoded = new byte[131072], decoded = new byte[input.Length];
                fixed (byte* src = input, dst = encoded, output = decoded)
                {
                    nuint size =
                        Check(Load<Codec>("ZL_CCtx_compress")(cctx, dst, (nuint)encoded.Length, src,
                            (nuint)input.Length));
                    nuint restored =
                        Check(Load<Codec>("ZL_DCtx_decompress")(dctx, output, (nuint)decoded.Length, dst, size));
                    if (restored != (nuint)input.Length || !input.AsSpan().SequenceEqual(decoded))
                        throw new InvalidDataException("Native OpenZL round trip failed.");
                }

                foreach (string export in new[]
                         {
                             "ZL_CCtx_compressMultiTypedRef", "ZL_DCtx_decompressMultiTBuffer", "ZL_FrameInfo_create",
                             "ZL_CompressorSerializer_serialize", "ZL_CompressorDeserializer_deserialize"
                         })
                    NativeLibrary.GetExport(library, export);
            }
            finally
            {
                if (dctx != 0)
                    freeDctx(dctx);
                if (cctx != 0)
                    freeCctx(cctx);
                if (compressor != 0)
                    freeCompressor(compressor);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static nuint Check(NativeSmokeReport report) => report.Code == 0
        ? report.Value
        : throw new InvalidDataException($"Native smoke test failed with OpenZL error {report.Code}.");
}