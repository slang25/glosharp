# Test complog fixtures

The `.complog` fixtures used by the test suite are generated on demand from the
source trees in this directory. The first test that needs a fixture calls
`ComplogFixture.GetOrBuild*(...)` which shells out to `dotnet build` with the
binary-log switch and then converts the binlog to a complog using
`Basic.CompilerLog.Util.CompilerLogUtil.ConvertBinaryLog`. Generated complogs
are cached under the test project's `bin/` directory (git-ignored).

To regenerate a fixture manually, delete the cached file under
`tests/GloSharp.Tests/bin/<config>/<tfm>/complogs/`.
