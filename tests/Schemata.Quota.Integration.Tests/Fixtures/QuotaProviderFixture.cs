using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Quota.Foundation.Runtime;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using IndexAttribute = Schemata.Abstractions.Entities.IndexAttribute;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Quota.Integration.Tests.Fixtures;

public enum QuotaProvider
{
    EntityFrameworkCore,
    LinqToDb,
}

public sealed class QuotaProviderFixture : IAsyncLifetime
{
    private readonly string _path = $"{Guid.NewGuid():n}.db";
    private readonly bool _scheduling;
    private readonly bool _contended;
    private ServiceProvider? _root;
    private IHostedService[] _hosted = [];

    public QuotaProviderFixture(QuotaProvider provider, bool scheduling = false, bool contended = false) {
        if (scheduling && provider != QuotaProvider.EntityFrameworkCore) {
            throw new ArgumentException("The scheduled fixture uses EF Core.", nameof(scheduling));
        }

        Provider = provider;
        _scheduling = scheduling;
        _contended = contended;
        Clock = new MutableTimeProvider();
        Contention = new();
    }

    public QuotaProvider Provider { get; }

    public MutableTimeProvider Clock { get; }

    public ContentionController Contention { get; }

    public IServiceProvider Services => _root ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IQuotaScopeResolver>(new StaticScopeResolver(new("scope")));

        if (Provider == QuotaProvider.EntityFrameworkCore) {
            AddEntityFrameworkCore(services);
        } else {
            AddLinqToDb(services);
        }

        if (_contended) {
            services.AddScoped<IQuotaStore>(provider => new ContendedQuotaStore(
                new RepositoryQuotaStore(
                    provider.GetRequiredService<IRepository<SchemataQuotaAccount>>(),
                    provider.GetRequiredService<IRepository<SchemataQuotaSegment>>(),
                    provider.GetRequiredService<IRepository<SchemataQuotaReservation>>(),
                    provider.GetRequiredService<IRepository<SchemataQuotaReservationItem>>(),
                    provider.GetRequiredService<IRepository<SchemataQuotaLedgerEntry>>()), Contention));
        }

        services.AddSchemataQuotaAccounting();
        if (_scheduling) AddScheduling(services);

        _root = services.BuildServiceProvider();
        await CreateSchemaAsync();

        if (_scheduling) {
            _hosted = _root.GetServices<IHostedService>().ToArray();
            foreach (var service in _hosted) await service.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            for (var index = _hosted.Length - 1; index >= 0; index--) {
                await _hosted[index].StopAsync(CancellationToken.None);
            }

            await _root.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    public IServiceScope CreateScope() {
        return Services.CreateScope();
    }

    public async ValueTask<QuotaReservationView> ReserveAsync(
        QuotaReservationRequest request,
        IUnitOfWork? transaction = null
    ) {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQuotaAccounting>().ReserveAsync(request, transaction);
    }

    public async Task<SchemataQuotaReservation?> ReservationAsync(Guid uid) {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQuotaStore>().GetReservationAsync(uid, CancellationToken.None);
    }

    public async Task<SchemataQuotaReservation?> ReservationAsync(string operationId) {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQuotaStore>()
                          .FindReservationAsync(new("scope"), new("subject"), operationId, CancellationToken.None);
    }

    public async Task<List<SchemataQuotaAccount>> AccountsAsync() {
        using var scope = CreateScope();
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataQuotaAccount>>();
        return await repository.ListAsync(query => query, CancellationToken.None).ToListAsync(CancellationToken.None);
    }

    public async Task<List<SchemataQuotaReservation>> ReservationsAsync() {
        using var scope = CreateScope();
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataQuotaReservation>>();
        var rows = await repository.ListAsync(query => query, CancellationToken.None).ToListAsync(CancellationToken.None);
        var store = scope.ServiceProvider.GetRequiredService<IQuotaStore>();
        var result = new List<SchemataQuotaReservation>(rows.Count);
        foreach (var row in rows) {
            if (await store.GetReservationAsync(row.Uid, CancellationToken.None) is { } reservation) result.Add(reservation);
        }

        return result;
    }

    public async Task<List<SchemataQuotaLedgerEntry>> LedgerAsync() {
        using var scope = CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IQuotaAccounting>()
                          .ListLedgerAsync(new(new("scope"), new("subject"), null, 200));
        return entries.ToList();
    }

    public static QuotaReservationRequest Request(
        string operationId,
        long amount = 1,
        long limit = 10,
        string algorithm = QuotaConstants.Algorithms.FixedWindow,
        DateTimeOffset? expiresAt = null,
        string metric = "calls"
    ) {
        return new(null, new("subject"), operationId, [
            new(new("requests", 1, algorithm, limit, TimeSpan.FromMinutes(1), limit), new(metric), amount),
        ], expiresAt);
    }

    private void AddEntityFrameworkCore(IServiceCollection services) {
        services.AddDbContextFactory<QuotaDbContext>(options => options
            .UseSqlite($"Data Source={_path};Pooling=False")
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddScoped<IUnitOfWork<QuotaDbContext>, EfCoreUnitOfWork<QuotaDbContext>>();
        AddRepository<SchemataQuotaAccount, EfCoreRepository<QuotaDbContext, SchemataQuotaAccount>>(services);
        AddRepository<SchemataQuotaSegment, EfCoreRepository<QuotaDbContext, SchemataQuotaSegment>>(services);
        AddRepository<SchemataQuotaReservation, EfCoreRepository<QuotaDbContext, SchemataQuotaReservation>>(services);
        AddRepository<SchemataQuotaReservationItem, EfCoreRepository<QuotaDbContext, SchemataQuotaReservationItem>>(services);
        AddRepository<SchemataQuotaLedgerEntry, EfCoreRepository<QuotaDbContext, SchemataQuotaLedgerEntry>>(services);
        if (_scheduling) {
            AddRepository<SchemataJob, EfCoreRepository<QuotaDbContext, SchemataJob>>(services);
            AddRepository<SchemataJobExecution, EfCoreRepository<QuotaDbContext, SchemataJobExecution>>(services);
        }
    }

    private void AddLinqToDb(IServiceCollection services) {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        var options = new DataOptions().UseSQLite($"Data Source={_path};Pooling=False").UseMappingSchema(schema);
        services.TryAddScoped(_ => new QuotaDataConnection(options));
        services.TryAddSingleton<Func<QuotaDataConnection>>(_ => () => new(options));
        services.AddScoped<IUnitOfWork<QuotaDataConnection>, LinqToDbUnitOfWork<QuotaDataConnection>>();
        AddRepository<SchemataQuotaAccount, LinqToDbRepository<QuotaDataConnection, SchemataQuotaAccount>>(services);
        AddRepository<SchemataQuotaSegment, LinqToDbRepository<QuotaDataConnection, SchemataQuotaSegment>>(services);
        AddRepository<SchemataQuotaReservation, LinqToDbRepository<QuotaDataConnection, SchemataQuotaReservation>>(services);
        AddRepository<SchemataQuotaReservationItem, LinqToDbRepository<QuotaDataConnection, SchemataQuotaReservationItem>>(services);
        AddRepository<SchemataQuotaLedgerEntry, LinqToDbRepository<QuotaDataConnection, SchemataQuotaLedgerEntry>>(services);
    }

    private void AddScheduling(IServiceCollection services) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, ConsumerResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, ConsumerResourceName<SchemataJobExecution>>());
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
    }

    private async Task CreateSchemaAsync() {
        using var scope = CreateScope();
        if (Provider == QuotaProvider.EntityFrameworkCore) {
            var context = scope.ServiceProvider.GetRequiredService<QuotaDbContext>();
            await context.Database.EnsureCreatedAsync();
            return;
        }

        var connection = scope.ServiceProvider.GetRequiredService<QuotaDataConnection>();
        CreateTableWithIndexes<SchemataQuotaAccount>(connection);
        CreateTableWithIndexes<SchemataQuotaSegment>(connection);
        CreateTableWithIndexes<SchemataQuotaReservation>(connection);
        CreateTableWithIndexes<SchemataQuotaReservationItem>(connection);
        CreateTableWithIndexes<SchemataQuotaLedgerEntry>(connection);
    }

    private static void CreateTableWithIndexes<TEntity>(DataConnection connection)
        where TEntity : class {
        connection.CreateTable<TEntity>(tableOptions: TableOptions.CreateIfNotExists);
        var table = typeof(TEntity).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(TEntity).Name;
        foreach (var index in typeof(TEntity).GetCustomAttributes<IndexAttribute>()) {
            var name = $"IX_{table}_{string.Join("_", index.Properties)}";
            var columns = string.Join(", ", index.Properties.Select(property => $"\"{property}\""));
            connection.Execute($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({columns})");
        }
    }

    private static void AddRepository<TEntity, TRepository>(IServiceCollection services)
        where TEntity : class
        where TRepository : class, IRepository<TEntity> {
        services.AddRepository<TEntity, TRepository>();
    }

    public sealed class QuotaDbContext(DbContextOptions<QuotaDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataQuotaAccount> Accounts { get; set; } = null!;
        public DbSet<SchemataQuotaSegment> Segments { get; set; } = null!;
        public DbSet<SchemataQuotaReservation> Reservations { get; set; } = null!;
        public DbSet<SchemataQuotaReservationItem> ReservationItems { get; set; } = null!;
        public DbSet<SchemataQuotaLedgerEntry> Ledger { get; set; } = null!;
        public DbSet<SchemataJob> Jobs { get; set; } = null!;
        public DbSet<SchemataJobExecution> Executions { get; set; } = null!;
    }

    public sealed class QuotaDataConnection(DataOptions options) : DataConnection(options);

    public sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() {
            return _now;
        }

        public void Advance(TimeSpan duration) {
            _now += duration;
        }
    }

    public sealed class ContentionController
    {
        private SchemataQuotaReservation? _stale;
        private bool _abortOnSave;

        public void Arm(SchemataQuotaReservation reservation) {
            _stale = reservation;
        }

        internal SchemataQuotaReservation? TakeStale(Guid uid) {
            if (_stale?.Uid != uid) return null;
            var stale = _stale;
            _stale = null;
            _abortOnSave = true;
            return stale;
        }

        internal void ThrowIfContended() {
            if (!_abortOnSave) return;
            _abortOnSave = false;
            throw new Schemata.Abstractions.Exceptions.AbortedException();
        }
    }

    private sealed class ContendedQuotaStore(IQuotaStore inner, ContentionController contention) : IQuotaStore
    {
        public IUnitOfWork Begin(IsolationLevel isolationLevel) => inner.Begin(isolationLevel);
        public void Join(IUnitOfWork transaction) => inner.Join(transaction);
        public ValueTask<SchemataQuotaAccount?> FindAccountAsync(QuotaScope scope, QuotaSubject subject, QuotaMetric metric, string policyName, int policyVersion, CancellationToken ct)
            => inner.FindAccountAsync(scope, subject, metric, policyName, policyVersion, ct);
        public ValueTask<IReadOnlyList<SchemataQuotaSegment>> ListSegmentsAsync(Guid accountUid, CancellationToken ct)
            => inner.ListSegmentsAsync(accountUid, ct);
        public ValueTask<SchemataQuotaReservation?> FindReservationAsync(QuotaScope scope, QuotaSubject subject, string operationId, CancellationToken ct)
            => inner.FindReservationAsync(scope, subject, operationId, ct);
        public async ValueTask<SchemataQuotaReservation?> GetReservationAsync(Guid reservationUid, CancellationToken ct) {
            return contention.TakeStale(reservationUid) ?? await inner.GetReservationAsync(reservationUid, ct);
        }
        public IAsyncEnumerable<SchemataQuotaReservation> ListExpiredPendingAsync(DateTimeOffset now, CancellationToken ct)
            => inner.ListExpiredPendingAsync(now, ct);
        public IAsyncEnumerable<SchemataQuotaLedgerEntry> ListLedgerAsync(QuotaLedgerQuery query, CancellationToken ct)
            => inner.ListLedgerAsync(query, ct);
        public Task AddAccountAsync(SchemataQuotaAccount account, CancellationToken ct) => inner.AddAccountAsync(account, ct);
        public Task SaveAccountAsync(SchemataQuotaAccount account, CancellationToken ct) => inner.SaveAccountAsync(account, ct);
        public Task ReplaceSegmentsAsync(Guid accountUid, IReadOnlyList<SchemataQuotaSegment> segments, CancellationToken ct)
            => inner.ReplaceSegmentsAsync(accountUid, segments, ct);
        public Task AddReservationAsync(SchemataQuotaReservation reservation, IReadOnlyList<SchemataQuotaReservationItem> items, CancellationToken ct)
            => inner.AddReservationAsync(reservation, items, ct);
        public async Task SaveReservationAsync(SchemataQuotaReservation reservation, CancellationToken ct) {
            await inner.SaveReservationAsync(reservation, ct);
            contention.ThrowIfContended();
        }
        public Task AddLedgerEntriesAsync(IReadOnlyList<SchemataQuotaLedgerEntry> entries, CancellationToken ct)
            => inner.AddLedgerEntriesAsync(entries, ct);
    }

    private sealed class StaticScopeResolver(QuotaScope scope) : IQuotaScopeResolver
    {
        public ValueTask<QuotaScope> ResolveAsync(CancellationToken ct = default) {
            return ValueTask.FromResult(scope);
        }
    }

    private sealed class ConsumerResourceName<TEntity> : IRepositoryAddAdvisor<TEntity>
        where TEntity : class, ICanonicalName
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext ctx,
            IRepository<TEntity> repository,
            TEntity entity,
            MutationOptions options,
            CancellationToken ct
        ) {
            if (string.IsNullOrWhiteSpace(entity.Name)) entity.Name = $"consumer-{Guid.NewGuid():n}";
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
