using TUnit.Core.Interfaces;

// A hung test (a child process that never exits, a pipe that never closes) should fail with
// its name rather than stall CI until the job is killed with no log. The slowest legitimate
// tests (file-based app restores from nuget.org) take well under a minute.
[assembly: Timeout(180_000)]

// Every processor test builds a Roslyn compilation, and some spawn `dotnet restore`. Running
// them all at once peaks around 3 GB, enough to wedge a 7 GB macOS CI runner; four at a time
// is just as fast on a laptop and peaks around 2 GB.
[assembly: ParallelLimiter<GloSharp.Tests.FourAtATime>]

namespace GloSharp.Tests
{
    public sealed class FourAtATime : IParallelLimit
    {
        public int Limit => 4;
    }
}
