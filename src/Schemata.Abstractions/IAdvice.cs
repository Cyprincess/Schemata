using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions;

public interface IAdvice : IFeature;

public interface IAdvice<in T1> : IAdvice
{
    Task<bool> AdviseAsync(T1 a1, CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2> : IAdvice
{
    Task<bool> AdviseAsync(T1 a1, T2 a2, CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9> : IAdvice
{
    Task<bool> AdviseAsync(
        T1                a1,
        T2                a2,
        T3                a3,
        T4                a4,
        T5                a5,
        T6                a6,
        T7                a7,
        T8                a8,
        T9                a9,
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}

public interface
    IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16> : IAdvice
{
    Task<bool> AdviseAsync(
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
        CancellationToken ct = default);
}
