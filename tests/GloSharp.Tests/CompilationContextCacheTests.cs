using GloSharp.Core;

namespace GloSharp.Tests;

public class CompilationContextCacheTests
{
    [Test]
    public async Task GetOrAdd_CacheMiss_CallsFactory()
    {
        var cache = new CompilationContextCache();
        var callCount = 0;

        var result = cache.GetOrAdd("key1", () =>
        {
            callCount++;
            return [];
        });

        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrAdd_CacheHit_SkipsFactory()
    {
        var cache = new CompilationContextCache();
        var callCount = 0;

        cache.GetOrAdd("key1", () =>
        {
            callCount++;
            return [];
        });

        cache.GetOrAdd("key1", () =>
        {
            callCount++;
            return [];
        });

        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrAdd_DifferentKeys_CallsFactoryForEach()
    {
        var cache = new CompilationContextCache();
        var callCount = 0;

        cache.GetOrAdd("key1", () => { callCount++; return []; });
        cache.GetOrAdd("key2", () => { callCount++; return []; });

        await Assert.That(callCount).IsEqualTo(2);
    }

    [Test]
    public async Task ComputeKey_SameInputs_ProducesSameKey()
    {
        var packages = new List<PackageReference>
        {
            new() { Name = "Foo", Version = "1.0.0" },
            new() { Name = "Bar", Version = "2.0.0" },
        };

        var key1 = CompilationContextCache.ComputeKey("net8.0", packages, null);
        var key2 = CompilationContextCache.ComputeKey("net8.0", packages, null);

        await Assert.That(key1).IsEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentFramework_ProducesDifferentKey()
    {
        var key1 = CompilationContextCache.ComputeKey("net8.0", null, null);
        var key2 = CompilationContextCache.ComputeKey("net9.0", null, null);

        await Assert.That(key1).IsNotEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentPackageOrder_ProducesSameKey()
    {
        var packages1 = new List<PackageReference>
        {
            new() { Name = "Foo", Version = "1.0.0" },
            new() { Name = "Bar", Version = "2.0.0" },
        };
        var packages2 = new List<PackageReference>
        {
            new() { Name = "Bar", Version = "2.0.0" },
            new() { Name = "Foo", Version = "1.0.0" },
        };

        var key1 = CompilationContextCache.ComputeKey("net8.0", packages1, null);
        var key2 = CompilationContextCache.ComputeKey("net8.0", packages2, null);

        await Assert.That(key1).IsEqualTo(key2);
    }

    [Test]
    public async Task ComputeKey_DifferentPackages_ProducesDifferentKey()
    {
        var packages1 = new List<PackageReference>
        {
            new() { Name = "Foo", Version = "1.0.0" },
        };
        var packages2 = new List<PackageReference>
        {
            new() { Name = "Bar", Version = "1.0.0" },
        };

        var key1 = CompilationContextCache.ComputeKey("net8.0", packages1, null);
        var key2 = CompilationContextCache.ComputeKey("net8.0", packages2, null);

        await Assert.That(key1).IsNotEqualTo(key2);
    }
}
