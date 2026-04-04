using GloSharp.Core;

namespace GloSharp.Tests;

public class ResultCacheTests
{
    private string _cacheDir = null!;

    [Before(Test)]
    public void Setup()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "glosharp-test-cache-" + Guid.NewGuid().ToString("N")[..8]);
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private static GloSharpResult CreateSampleResult(string code = "var x = 42;") => new()
    {
        Code = code,
        Original = code,
        Hovers = [],
        Errors = [],
        Meta = new GloSharpMeta
        {
            TargetFramework = "net8.0",
            CompileSucceeded = true,
        },
    };

    [Test]
    public async Task TryGet_NoFile_ReturnsNull()
    {
        var cache = new ResultCache(_cacheDir);
        var result = cache.TryGet("nonexistent");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Set_ThenTryGet_ReturnsCachedResult()
    {
        var cache = new ResultCache(_cacheDir);
        var original = CreateSampleResult();

        cache.Set("test-key", original);
        var cached = cache.TryGet("test-key");

        await Assert.That(cached).IsNotNull();
        await Assert.That(cached!.Code).IsEqualTo(original.Code);
        await Assert.That(cached.Meta.TargetFramework).IsEqualTo("net8.0");
        await Assert.That(cached.Meta.CompileSucceeded).IsTrue();
    }

    [Test]
    public async Task Set_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_cacheDir, "nested", "deep");
        var cache = new ResultCache(nestedDir);

        cache.Set("test-key", CreateSampleResult());

        await Assert.That(Directory.Exists(nestedDir)).IsTrue();
    }

    [Test]
    public async Task TryGet_CorruptFile_ReturnsNull()
    {
        Directory.CreateDirectory(_cacheDir);
        var path = Path.Combine(_cacheDir, "corrupt-key.json");
        File.WriteAllText(path, "not valid json {{{");

        var cache = new ResultCache(_cacheDir);
        var result = cache.TryGet("corrupt-key");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Set_OverwritesCorruptFile()
    {
        Directory.CreateDirectory(_cacheDir);
        var path = Path.Combine(_cacheDir, "overwrite-key.json");
        File.WriteAllText(path, "not valid json {{{");

        var cache = new ResultCache(_cacheDir);
        cache.Set("overwrite-key", CreateSampleResult("var y = 1;"));

        var result = cache.TryGet("overwrite-key");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Code).IsEqualTo("var y = 1;");
    }

    [Test]
    public async Task ComputeKey_SameInputs_ProducesSameKey()
    {
        var key1 = ResultCache.ComputeKey("source", "net8.0", null, null);
        var key2 = ResultCache.ComputeKey("source", "net8.0", null, null);

        await Assert.That(key1).IsEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentSource_ProducesDifferentKey()
    {
        var key1 = ResultCache.ComputeKey("var x = 1;", "net8.0", null, null);
        var key2 = ResultCache.ComputeKey("var x = 2;", "net8.0", null, null);

        await Assert.That(key1).IsNotEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentFramework_ProducesDifferentKey()
    {
        var key1 = ResultCache.ComputeKey("source", "net8.0", null, null);
        var key2 = ResultCache.ComputeKey("source", "net9.0", null, null);

        await Assert.That(key1).IsNotEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentPackageOrder_ProducesSameKey()
    {
        var packages1 = new List<PackageReference>
        {
            new() { Name = "Foo", Version = "1.0" },
            new() { Name = "Bar", Version = "2.0" },
        };
        var packages2 = new List<PackageReference>
        {
            new() { Name = "Bar", Version = "2.0" },
            new() { Name = "Foo", Version = "1.0" },
        };

        var key1 = ResultCache.ComputeKey("source", "net8.0", packages1, null);
        var key2 = ResultCache.ComputeKey("source", "net8.0", packages2, null);

        await Assert.That(key1).IsEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentProjectPath_ProducesDifferentKey()
    {
        var key1 = ResultCache.ComputeKey("source", "net8.0", null, "/path/a.csproj");
        var key2 = ResultCache.ComputeKey("source", "net8.0", null, "/path/b.csproj");

        await Assert.That(key1).IsNotEqualTo(key2);
    }

    [Test]
    public async Task Set_WritesAtomically_FileExists()
    {
        var cache = new ResultCache(_cacheDir);
        cache.Set("atomic-key", CreateSampleResult());

        var path = Path.Combine(_cacheDir, "atomic-key.json");
        await Assert.That(File.Exists(path)).IsTrue();

        // Verify no temp files remain
        var files = Directory.GetFiles(_cacheDir, "*.tmp.*");
        await Assert.That(files.Length).IsEqualTo(0);
    }
}
