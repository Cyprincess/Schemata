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
    public static async Task AdviseAsync<T1>(IServiceProvider serviceProvider, T1 arg1) {
        var advices = serviceProvider.GetServices<TAdvice>().OfType<IAdvice<T1>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2>(IServiceProvider serviceProvider, T1 arg1, T2 arg2) {
        var advices = serviceProvider.GetServices<TAdvice>().OfType<IAdvice<T1, T2>>().OrderBy(a => a.Order).ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10,
        T11              arg11) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10,
        T11              arg11,
        T12              arg12) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10,
        T11              arg11,
        T12              arg12,
        T13              arg13) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10,
        T11              arg11,
        T12              arg12,
        T13              arg13,
        T14              arg14) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10,
        T11              arg11,
        T12              arg12,
        T13              arg13,
        T14              arg14,
        T15              arg15) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
            if (!next) {
                break;
            }
        }
    }

    public static async Task AdviseAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(
        IServiceProvider serviceProvider,
        T1               arg1,
        T2               arg2,
        T3               arg3,
        T4               arg4,
        T5               arg5,
        T6               arg6,
        T7               arg7,
        T8               arg8,
        T9               arg9,
        T10              arg10,
        T11              arg11,
        T12              arg12,
        T13              arg13,
        T14              arg14,
        T15              arg15,
        T16              arg16) {
        var advices = serviceProvider.GetServices<TAdvice>()
                                     .OfType<IAdvice<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>()
                                     .OrderBy(a => a.Order)
                                     .ToList();
        foreach (var advice in advices) {
            var next = await advice.AdviseAsync(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
            if (!next) {
                break;
            }
        }
    }
}
