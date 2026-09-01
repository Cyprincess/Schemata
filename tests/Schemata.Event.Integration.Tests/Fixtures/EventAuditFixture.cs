using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Integration.Tests.Fixtures;

public sealed class EventAuditFixture : IAsyncLifetime
{
    private readonly bool                _withNameAdvisor;
    private          SqliteConnection?   _connection;
    private          ServiceProvider?    _root;
    private          Mock<ILogger<InProcessEventBus>> _busLogger = null!;

    public EventAuditFixture(bool withNameAdvisor) {
        _withNameAdvisor = withNameAdvisor;
    }

    public Mock<ILogger<InProcessEventBus>> BusLogger => _busLogger;

    public async Task InitializeAsync() {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<EventAuditDbContext>(opts => opts.UseSqlite(_connection)
                                                              .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        services.AddRepository<SchemataEvent, EfCoreRepository<EventAuditDbContext, SchemataEvent>>();
        services.AddScoped<IUnitOfWork<EventAuditDbContext>, EfCoreUnitOfWork<EventAuditDbContext>>();

        services.AddLogging();
        _busLogger = new Mock<ILogger<InProcessEventBus>>();
        services.AddSingleton<ILogger<InProcessEventBus>>(_busLogger.Object);

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseEvent()
               .RegisterEvent<StudentCreated>("students/student-created")
               .UseProducer(p => p.UseInProcess());
        builder.Invoke(services);

        if (_withNameAdvisor) {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataEvent>, EventAuditNameAdvisor>());
        }

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        var       db    = scope.ServiceProvider.GetRequiredService<EventAuditDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.DisposeAsync();
        }

        if (_connection is not null) {
            await _connection.DisposeAsync();
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent {
        await using var scope    = _root!.CreateAsyncScope();
        var            bus      = scope.ServiceProvider.GetRequiredService<IEventBus>();
        await bus.PublishAsync(@event);
    }

    public async Task<int> CountAsync() {
        await using var scope = _root!.CreateAsyncScope();
        var            db     = scope.ServiceProvider.GetRequiredService<EventAuditDbContext>();
        return await db.SchemataEvents.CountAsync();
    }

    public async Task<SchemataEvent?> SingleOrDefaultAsync() {
        await using var scope = _root!.CreateAsyncScope();
        var            db     = scope.ServiceProvider.GetRequiredService<EventAuditDbContext>();
        return await db.SchemataEvents.SingleOrDefaultAsync();
    }
}
