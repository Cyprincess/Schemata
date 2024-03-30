using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

// ReSharper disable once CheckNamespace
namespace Schemata;

public static class Advices<TAdvice>
    where TAdvice : IAdvice
{
    public static async Task AdviseAsync<T1>(IServiceProvider serviceProvider, T1 a1) {
        var advices = serviceProvider.GetServices<TAdvice>().OfType<IAdvice<T1>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2>(IServiceProvider serviceProvider, T1 a1, T2 a2) {
        var advices = serviceProvider.GetServices<TAdvice>().OfType<IAdvice<T1, T2>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10,
        T11              a11) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10,
        T11              a11,
        T12              a12) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10,
        T11              a11,
        T12              a12,
        T13              a13) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10,
        T11              a11,
        T12              a12,
        T13              a13,
        T14              a14) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10,
        T11              a11,
        T12              a12,
        T13              a13,
        T14              a14,
        T15              a15) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(
        IServiceProvider serviceProvider,
        T1               a1,
        T2               a2,
        T3               a3,
        T4               a4,
        T5               a5,
        T6               a6,
        T7               a7,
        T8               a8,
        T9               a9,
        T10              a10,
        T11              a11,
        T12              a12,
        T13              a13,
        T14              a14,
        T15              a15,
        T16              a16) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16);
            if (!next) {
                break;
            }
        }
    }
}
