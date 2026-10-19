using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests;

[Trait("Layer", "Unit")]
public class RepositoryCommitShould
{
    [Fact]
    public async Task QueryWithIgnoreCacheEviction_DoesNotSuppressLaterMutationCommit() {
        var advisor = new CommitRecorder();
        var services = new ServiceCollection();
        services.AddSingleton<IRepositoryCommittedAdvisor<Student>>(advisor);
        await using var provider   = services.BuildServiceProvider();
        await using var repository = new CommitRepository(provider);

        await repository.CountAsync<Student>(q => q, default, new QueryOptions(IgnoreCacheEviction: true));
        await repository.UpdateAsync(new Student());
        await repository.CommitAsync();

        Assert.False(Assert.Single(advisor.Options).IgnoreCacheEviction);
    }

    [Fact]
    public async Task MutationWithIgnoreCacheEviction_ReachesOnlyItsCommit() {
        var advisor = new CommitRecorder();
        var services = new ServiceCollection();
        services.AddSingleton<IRepositoryCommittedAdvisor<Student>>(advisor);
        await using var provider   = services.BuildServiceProvider();
        await using var repository = new CommitRepository(provider);

        await repository.UpdateAsync(new Student(), default, new MutationOptions(IgnoreCacheEviction: true));
        await repository.CommitAsync();
        await repository.UpdateAsync(new Student());
        await repository.CommitAsync();

        Assert.Equal([true, false], advisor.Options.Select(options => options.IgnoreCacheEviction));
    }

    #region Nested type: CommitRecorder

    private sealed class CommitRecorder : IRepositoryCommittedAdvisor<Student>
    {
        public List<MutationOptions> Options { get; } = [];

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext        ctx,
            IRepository<Student> repository,
            MutationOptions      options,
            CancellationToken    ct
        ) {
            Options.Add(options);
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    #endregion

    #region Nested type: CommitRepository

    private sealed class CommitRepository(IServiceProvider sp) : RepositoryBase<Student>(sp)
    {
        public override Task<MutationResult> AddAsync(Student entity, CancellationToken ct = default, MutationOptions options = default) {
            return MutateAsync(options);
        }

        public override Task<MutationResult> UpdateAsync(Student entity, CancellationToken ct = default, MutationOptions options = default) {
            return MutateAsync(options);
        }

        public override Task<MutationResult> RemoveAsync(Student entity, CancellationToken ct = default, MutationOptions options = default) {
            return MutateAsync(options);
        }

        private Task<MutationResult> MutateAsync(MutationOptions options) {
            EnsureWriteUnitOfWork();
            TrackEvictionSuppression(options.IgnoreCacheEviction);
            StageWrite();

            return Task.FromResult(MutationResult.Applied);
        }

        protected override ConfiguredCancelableAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(
            IQueryable<TResult> query,
            CancellationToken   ct
        ) => throw new NotSupportedException();

        protected override Task<TResult?> FirstOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct)
            where TResult : default => Task.FromResult(query.FirstOrDefault());

        protected override Task<TResult?> SingleOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct)
            where TResult : default => Task.FromResult(query.SingleOrDefault());

        protected override Task<bool> AnyAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) => Task.FromResult(query.Any());

        protected override Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) => Task.FromResult(query.Count());

        protected override Task<long> LongCountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) => Task.FromResult(query.LongCount());

        protected override IQueryable<Student> AsQueryable() => Array.Empty<Student>().AsQueryable();

        protected override IUnitOfWork CreateUnitOfWork() => new RecordingUnitOfWork();

        protected override void AttachContext(IUnitOfWork uow) { }

        protected override void DisposeContext() { }
    }

    #endregion

    #region Nested type: RecordingUnitOfWork

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        private readonly List<(int Order, Func<CancellationToken, Task> Sink)> _commits = [];
        private readonly List<Action>                                           _rollbacks = [];
        private bool _completed;

        public void AddCommitSink(int order, Func<CancellationToken, Task> sink) { _commits.Add((order, sink)); }

        public void AddRollbackSink(Action reset) { _rollbacks.Add(reset); }

        public void AddSavePreparation(Action preparation) { }

        public async Task CommitAsync(CancellationToken ct = default) {
            foreach (var (_, sink) in _commits.OrderBy(item => item.Order)) {
                await sink(ct);
            }

            _completed = true;
        }

        public Task RollbackAsync(CancellationToken ct = default) {
            Reset();
            return Task.CompletedTask;
        }

        public void Dispose() { Reset(); }

        public ValueTask DisposeAsync() {
            Reset();
            return ValueTask.CompletedTask;
        }

        private void Reset() {
            if (_completed) {
                return;
            }

            _completed = true;
            foreach (var reset in _rollbacks) {
                reset();
            }
        }
    }

    #endregion
}
