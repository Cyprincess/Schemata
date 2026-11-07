using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Schemata.Common.Tests;

public class AppDomainTypeCacheShould
{
    [Fact]
    public void GetFields_Returns_Public_Instance_Fields_And_Reuses_The_Cached_Dictionary() {
        var first  = AppDomainTypeCache.GetFields(typeof(FieldSample));
        var second = AppDomainTypeCache.GetFields(typeof(FieldSample));

        Assert.Same(first, second);
        Assert.True(first.ContainsKey(nameof(FieldSample.Instance)));
        Assert.False(first.ContainsKey(nameof(FieldSample.Static)));
    }

    [Fact]
    public void GetProperty_Returns_Null_When_The_Property_Does_Not_Exist() {
        Assert.Null(AppDomainTypeCache.GetProperty(typeof(string), "NoSuchPropertyOnString"));
    }

    [Fact]
    public void GetAllTypes_Refreshes_Assemblies_Loaded_After_The_First_Touch() {
        // Force the cache to initialize and index the assemblies loaded so far.
        AppDomainTypeCache.GetProperties(typeof(string));

        // Load Moq explicitly when another test has not already loaded it. The refreshed cache must
        // contain its exported types in either case.
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                 .FirstOrDefault(candidate => candidate.GetName().Name == "Moq")
                    ?? Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Moq.dll"));

        var types = AppDomainTypeCache.GetAllTypes().Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Moq.Mock", types);
        Assert.All(
            assembly.GetExportedTypes(),
            type => Assert.Contains(type.FullName, types));
    }

    [Fact]
    public void GetProperties_NewShadowedMember_SelectsMostDerived() {
        var properties = AppDomainTypeCache.GetProperties(typeof(DerivedMetadata));

        var property = properties["Value"];
        Assert.Equal(typeof(DerivedMetadata), property.DeclaringType);
        Assert.Equal(typeof(int), property.PropertyType);
    }

    [Fact]
    public void GetFields_NewShadowedMember_SelectsMostDerived() {
        var fields = AppDomainTypeCache.GetFields(typeof(DerivedFieldMetadata));

        var field = fields["Value"];
        Assert.Equal(typeof(DerivedFieldMetadata), field.DeclaringType);
        Assert.Equal(typeof(int), field.FieldType);
    }

    [Fact]
    public void Assembly_Indexing_Failure_Rethrows_And_Retries() {
        var name     = "Schemata.Cache.Retry." + Guid.NewGuid().ToString("N");
        var sentinel = new InvalidOperationException("indexing failed");
        var assembly = new Mock<Assembly>();
        assembly.SetupGet(candidate => candidate.IsDynamic).Returns(false);
        assembly.Setup(candidate => candidate.GetName()).Returns(new AssemblyName(name));
        assembly.SetupSequence(candidate => candidate.GetExportedTypes())
                .Throws(sentinel)
                .Returns([typeof(FirstPublishedType)]);

        var thrown = Assert.Throws<InvalidOperationException>(() => AppDomainTypeCache.Index(assembly.Object));

        Assert.Same(sentinel, thrown);
        Assert.False(AppDomainTypeCache.Assemblies.ContainsKey(name));

        AppDomainTypeCache.Index(assembly.Object);

        assembly.Verify(candidate => candidate.GetExportedTypes(), Times.Exactly(2));
        Assert.Same(assembly.Object, AppDomainTypeCache.Assemblies[name]);
        Assert.Same(typeof(FirstPublishedType), AppDomainTypeCache.Types[typeof(FirstPublishedType).FullName!]);
    }

    [Fact]
    public async Task Concurrent_Type_Readers_See_Only_Complete_Published_Snapshots() {
        var name     = "Schemata.Cache.Publication." + Guid.NewGuid().ToString("N");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var assembly = new Mock<Assembly>();
        assembly.SetupGet(candidate => candidate.IsDynamic).Returns(false);
        assembly.Setup(candidate => candidate.GetName()).Returns(new AssemblyName(name));
        assembly.Setup(candidate => candidate.GetExportedTypes()).Returns(() => {
            entered.Set();
            release.Wait();
            return [typeof(FirstPublishedType), typeof(SecondPublishedType)];
        });

        var indexing = Task.Run(() => AppDomainTypeCache.Index(assembly.Object));
        Task<HashSet<string>> reader;
        bool completedBeforeRelease;
        try {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            reader = Task.Run(() => AppDomainTypeCache.GetAllTypes()
                                                   .Select(type => type.FullName ?? string.Empty)
                                                   .ToHashSet(StringComparer.Ordinal));
            completedBeforeRelease = await Task.WhenAny(reader, Task.Delay(100)) == reader;
        } finally {
            release.Set();
        }

        await indexing;
        var snapshot = await reader;

        Assert.False(completedBeforeRelease);
        Assert.Contains(typeof(FirstPublishedType).FullName!, snapshot);
        Assert.Contains(typeof(SecondPublishedType).FullName!, snapshot);
    }

    [Fact]
    public async Task Concurrent_Metadata_Readers_Reuse_Published_Dictionaries() {
        var properties = AppDomainTypeCache.GetProperties(typeof(DerivedMetadata));
        var fields     = AppDomainTypeCache.GetFields(typeof(DerivedFieldMetadata));

        var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(() => {
            Assert.Same(properties, AppDomainTypeCache.GetProperties(typeof(DerivedMetadata)));
            Assert.Same(fields, AppDomainTypeCache.GetFields(typeof(DerivedFieldMetadata)));
        }));

        await Task.WhenAll(readers);
    }

    private class BaseMetadata
    {
        public string Value { get; set; } = "base";
    }

    private sealed class DerivedMetadata : BaseMetadata
    {
        public new int Value { get; set; }
    }

    private class BaseFieldMetadata
    {
        public string Value = "base";
    }

    private sealed class DerivedFieldMetadata : BaseFieldMetadata
    {
        public new int Value = 1;
    }

    public sealed class FirstPublishedType
    {
    }

    public sealed class SecondPublishedType
    {
    }

    private sealed class FieldSample
    {
        public static int Static = 0;

        public int Instance = 0;
    }
}
