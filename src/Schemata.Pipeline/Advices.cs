using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advices;

// ReSharper disable once CheckNamespace
namespace Schemata;

public static class Advices<TAdvice> where TAdvice : IAdvice
{
    public static async Task<bool> AdviseAsync<T1>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>().OfType<IAdvice<T1>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>().OfType<IAdvice<T1, T2>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>().OfType<IAdvice<T1, T2, T3>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>().OfType<IAdvice<T1, T2, T3, T4>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>().OfType<IAdvice<T1, T2, T3, T4, T5>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        T14               a14,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        T14               a14,
        T15               a15,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(
        IServiceProvider  sp,
        AdviceContext     ctx,
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        T10               a10,
        T11               a11,
        T12               a12,
        T13               a13,
        T14               a14,
        T15               a15,
        T16               a16,
        CancellationToken ct = default) {
        var advices = sp.GetServices<TAdvice>()
                        .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>()
                        .OrderBy(a => a.Order)
                        .ToList();
        foreach (var advice in advices) {
            ct.ThrowIfCancellationRequested();
            var next = await advice.AdviseAsync(ctx, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, ct);
            if (!next) {
                return false;
            }
        }

        return true;
    }
}
