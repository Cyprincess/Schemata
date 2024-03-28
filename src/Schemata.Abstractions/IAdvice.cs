using System.Threading.Tasks;

namespace Schemata.Abstractions;

public interface IAdvice : IFeature;

public interface IAdvice<in T1> : IAdvice
{
    Task<bool> AdviseAsync(T1 arg1);
}

public interface IAdvice<in T1, in T2> : IAdvice
{
    Task<bool> AdviseAsync(T1 arg1, T2 arg2);
}

public interface IAdvice<in T1, in T2, in T3> : IAdvice
{
    Task<bool> AdviseAsync(T1 arg1, T2 arg2, T3 arg3);
}

public interface IAdvice<in T1, in T2, in T3, in T4> : IAdvice
{
    Task<bool> AdviseAsync(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5> : IAdvice
{
    Task<bool> AdviseAsync(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4,
        T5 arg5);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6> : IAdvice
{
    Task<bool> AdviseAsync(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4,
        T5 arg5,
        T6 arg6);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IAdvice
{
    Task<bool> AdviseAsync(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4,
        T5 arg5,
        T6 arg6,
        T7 arg7);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8> : IAdvice
{
    Task<bool> AdviseAsync(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4,
        T5 arg5,
        T6 arg6,
        T7 arg7,
        T8 arg8);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9> : IAdvice
{
    Task<bool> AdviseAsync(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4,
        T5 arg5,
        T6 arg6,
        T7 arg7,
        T8 arg8,
        T9 arg9);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10,
        T11 arg11);
}

public interface
    IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10,
        T11 arg11,
        T12 arg12);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12,
                         in T13> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10,
        T11 arg11,
        T12 arg12,
        T13 arg13);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13,
                         in T14> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10,
        T11 arg11,
        T12 arg12,
        T13 arg13,
        T14 arg14);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13,
                         in T14, in T15> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10,
        T11 arg11,
        T12 arg12,
        T13 arg13,
        T14 arg14,
        T15 arg15);
}

public interface IAdvice<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13,
                         in T14, in T15, in T16> : IAdvice
{
    Task<bool> AdviseAsync(
        T1  arg1,
        T2  arg2,
        T3  arg3,
        T4  arg4,
        T5  arg5,
        T6  arg6,
        T7  arg7,
        T8  arg8,
        T9  arg9,
        T10 arg10,
        T11 arg11,
        T12 arg12,
        T13 arg13,
        T14 arg14,
        T15 arg15,
        T16 arg16);
}
