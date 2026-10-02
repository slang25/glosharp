// A hung test (a child process that never exits, a pipe that never closes) should fail with
// its name rather than stall CI until the job is killed with no log. The slowest legitimate
// tests (file-based app restores from nuget.org) take well under a minute.
[assembly: Timeout(180_000)]
