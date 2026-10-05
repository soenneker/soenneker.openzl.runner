using System.Threading;
using System.Threading.Tasks;
namespace Soenneker.OpenZl.Runner.Utils.Abstract;
/// <summary>Builds, verifies, and stages the current platform's pinned OpenZL distribution.</summary>
public interface IFileOperationsUtil
{
    /// <summary>Builds the runtime and optionally updates its binary repository when OpenZl:UpdateRepository is true.</summary>
    ValueTask Process(CancellationToken cancellationToken = default);
}
