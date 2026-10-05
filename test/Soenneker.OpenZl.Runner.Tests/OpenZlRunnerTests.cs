using System;
using Soenneker.OpenZl.Runner.Utils;
namespace Soenneker.OpenZl.Runner.Tests;

public sealed class OpenZlRunnerTests
{
    [Test]
    public void StagedLibraryRoundTripsAndExportsPublicApi()
    {
        string path = Environment.GetEnvironmentVariable("OPENZL_NATIVE_LIBRARY") ?? throw new InvalidOperationException("Build and stage a library with the runner, then set OPENZL_NATIVE_LIBRARY to its absolute path.");
        NativeSmoke.Verify(path);
    }
}
