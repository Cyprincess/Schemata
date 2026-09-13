using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Xunit;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using ColumnAttribute = System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

internal interface ITagged
{
    ICollection<int>? Tags { get; set; }
}

[Trait("Category", "Integration")]
public class ModelDefaultsShould : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private DataConnection _connection = null!;
    private DataConnection _fluentConnection = null!;

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var fluentSchema = new MappingSchema();
        fluentSchema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        ConfigureFluent(fluentSchema);

        var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);
        var fluentOptions = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(fluentSchema);

        _connection = new DataConnection(options);
        _fluentConnection = new DataConnection(fluentOptions);

        _connection.CreateTable<Defaults>();
        _connection.CreateTable<HiddenOverride>();
        _fluentConnection.CreateTable<FluentMapped>();
        _connection.CreateTable<ForwarderChild>();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() {
        await _connection.DisposeAsync();
        await _fluentConnection.DisposeAsync();

        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #endregion

    [Fact]
    public void ConventionDiscovery_MapsPublicGetterWithPublicOrPrivateSetter() {
        var descriptor = Describe(_connection, typeof(Defaults));

        Assert.Equal(["Id", "Plain", "PrivateSetter"], MemberNames(descriptor));

        var inserted = Defaults.Create(Guid.NewGuid(), "plain", "hidden");
        _connection.Insert(inserted);

        var loaded = _connection.GetTable<Defaults>().Single(d => d.Plain == "plain");
        Assert.Equal("hidden", loaded.PrivateSetter);
        Assert.Equal("plain", loaded.Plain);
    }

    [Fact]
    public void ExplicitColumnAttribute_RevivesPrivateGetterProperty() {
        var descriptor = Describe(_connection, typeof(HiddenOverride));

        Assert.Equal(["Hidden", "Id"], MemberNames(descriptor));
        Assert.Equal("hidden_override", descriptor.Columns.Single(c => c.MemberName == "Hidden").ColumnName);

        var inserted = HiddenOverride.Create(Guid.NewGuid(), "secret-value");
        _connection.Insert(inserted);

        var loaded = _connection.GetTable<HiddenOverride>().Single(h => h.Id == inserted.Id);
        Assert.Equal("secret-value", loaded.ReadHidden());
    }

    [Fact]
    public void DatabaseGeneratedIdentity_And_ClassKey_ReviveNonCandidateMembers() {
        var descriptor = Describe(_connection, typeof(Overrides));

        Assert.Equal(["Id", "KeyPart", "Seq"], MemberNames(descriptor));
        Assert.True(descriptor.Columns.Single(c => c.MemberName == "KeyPart").IsPrimaryKey);
        Assert.True(descriptor.Columns.Single(c => c.MemberName == "Seq").IsIdentity);
    }

    [Fact]
    public void FluentMapping_SurvivesConvention_AfterConventionReaderRegistration() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        ConfigureFluent(schema);

        var descriptor = schema.GetEntityDescriptor(typeof(FluentMapped));

        Assert.Equal(["Id", "Open", "Secret"], MemberNames(descriptor));
        Assert.Equal("fluent_secret", descriptor.Columns.Single(c => c.MemberName == "Secret").ColumnName);
        Assert.DoesNotContain(descriptor.Columns, c => c.MemberName == "Unmapped");

        var inserted = FluentMapped.Create(Guid.NewGuid(), "fluent-secret");
        _fluentConnection.Insert(inserted);

        var loaded = _fluentConnection.GetTable<FluentMapped>().Single(f => f.Id == inserted.Id);
        Assert.Equal("fluent-secret", loaded.ReadSecret());
    }

    [Fact]
    public void FluentMapping_SurvivesConvention_BeforeConventionReaderRegistration() {
        var schema = new MappingSchema();
        ConfigureFluent(schema);
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var descriptor = schema.GetEntityDescriptor(typeof(FluentMapped));

        Assert.Equal(["Id", "Open", "Secret"], MemberNames(descriptor));
        Assert.Equal("fluent_secret", descriptor.Columns.Single(c => c.MemberName == "Secret").ColumnName);
        Assert.DoesNotContain(descriptor.Columns, c => c.MemberName == "Unmapped");
    }

    [Fact]
    public void InheritedExplicitInterfaceForwarder_IsExcluded_BeforeJsonPromotion() {
        var descriptor = Describe(_connection, typeof(ForwarderChild));

        Assert.Equal(["Extra", "Id", "Real"], MemberNames(descriptor));
        Assert.DoesNotContain(descriptor.Columns, c => c.MemberName.Contains('.', StringComparison.Ordinal));
        Assert.DoesNotContain(descriptor.Columns, c => c.MemberName == "Tags");

        var inserted = new ForwarderChild { Id = Guid.NewGuid(), Real = "real", Extra = "extra" };
        _connection.Insert(inserted);

        var loaded = _connection.GetTable<ForwarderChild>().Single(f => f.Id == inserted.Id);
        Assert.Equal("real", loaded.Real);
        Assert.Equal("extra", loaded.Extra);
    }

    private static EntityDescriptor Describe(DataConnection connection, Type type) {
        return connection.MappingSchema.GetEntityDescriptor(type);
    }

    private static string[] MemberNames(EntityDescriptor descriptor) {
        return descriptor.Columns
                         .Select(c => c.MemberName)
                         .OrderBy(n => n, StringComparer.Ordinal)
                         .ToArray();
    }

    // FluentMappingBuilder.Build prepends its reader through AddMetadataReader, so calling it
    // before or after registering the convention reader covers both attribute-collection orders.
    private static void ConfigureFluent(MappingSchema schema) {
        var builder = new FluentMappingBuilder(schema);
        builder.HasAttribute<FluentMapped>(FluentMapped.SecretExpression, new global::LinqToDB.Mapping.ColumnAttribute { Name = "fluent_secret" });
        builder.Build();
    }

    #region Nested type: Defaults

    [Table("md_defaults")]
    [Abstractions.Entities.PrimaryKey(nameof(Id))]
    private sealed class Defaults
    {
        public Defaults() { }

        public Guid Id { get; set; }
        public string Plain { get; set; } = "";
        public string PrivateSetter { get; private set; } = "";
        private string PrivateGetter { get; set; } = "";
        public string ReadOnlyProp => "computed";
        public static string StaticProp { get; set; } = "";
        public string this[int index] { get => "item"; set { } }

        internal static Defaults Create(Guid id, string plain, string privateSetter) {
            return new() { Id = id, Plain = plain, PrivateSetter = privateSetter };
        }
    }

    #endregion

    #region Nested type: HiddenOverride

    [Table("md_override_hidden")]
    [Abstractions.Entities.PrimaryKey(nameof(Id))]
    private sealed class HiddenOverride
    {
        public HiddenOverride() { }

        public Guid Id { get; set; }

        [Column("hidden_override")]
        public string Hidden { private get; set; } = "";
        public string Ignored { private get; set; } = "";

        internal string ReadHidden() => Hidden;

        internal static HiddenOverride Create(Guid id, string hidden) {
            return new() { Id = id, Hidden = hidden };
        }
    }

    #endregion

    #region Nested type: Overrides

    [Table("md_overrides")]
    [Abstractions.Entities.PrimaryKey(nameof(Id), nameof(KeyPart))]
    private sealed class Overrides
    {
        public Guid Id { get; set; }
        public string KeyPart { private get; set; } = "";

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Seq { private get; set; }
    }

    #endregion

    #region Nested type: FluentMapped

    [Table("md_fluent")]
    [Abstractions.Entities.PrimaryKey(nameof(Id))]
    private sealed class FluentMapped
    {
        public Guid Id { get; set; }
        public string Open { get; set; } = "";
        public string Secret { private get; set; } = "";
        public string Unmapped { private get; set; } = "";

        internal string ReadSecret() => Secret;
        internal static System.Linq.Expressions.Expression<Func<FluentMapped, object?>> SecretExpression => row => row.Secret;

        internal static FluentMapped Create(Guid id, string secret) {
            return new() { Id = id, Secret = secret };
        }
    }

    #endregion

    #region Nested type: ForwarderChild

    [Table("md_forwarder")]
    [Abstractions.Entities.PrimaryKey(nameof(Id))]
    private abstract class ForwarderBase : ITagged
    {
        public Guid Id { get; set; }
        public string Real { get; set; } = "";

        private List<int>? _tags;

        ICollection<int>? ITagged.Tags {
            get => _tags;
            set => _tags = value as List<int>;
        }
    }

    private sealed class ForwarderChild : ForwarderBase
    {
        public string Extra { get; set; } = "";
    }

    #endregion
}
