// Windows 2000 / .NET 2.0 build only (A2G-Patch.csproj, Win2000=true): the delegate types that
// .NET 3.5 (System.Core) and 4.0 (mscorlib) added. Action<T> already exists in .NET 2.0's mscorlib.

namespace System
{
    /// <summary>Parameterless method without a result (.NET 3.5 System.Core type, rebuilt for .NET 2.0).</summary>
    public delegate void Action();

    /// <summary>Method with two parameters and no result.</summary>
    public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2);

    /// <summary>Method with three parameters and no result.</summary>
    public delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3);

    /// <summary>Method with four parameters and no result.</summary>
    public delegate void Action<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);

    /// <summary>Parameterless method returning a value.</summary>
    public delegate TResult Func<out TResult>();

    /// <summary>Method with one parameter returning a value.</summary>
    public delegate TResult Func<in T, out TResult>(T arg);

    /// <summary>Method with two parameters returning a value.</summary>
    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);

    /// <summary>Method with three parameters returning a value.</summary>
    public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);

    /// <summary>Method with four parameters returning a value.</summary>
    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
}

namespace System.Runtime.CompilerServices
{
    /// <summary>Marks extension methods. The C# compiler needs this type to emit them; .NET 2.0 lacks it.</summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ExtensionAttribute : Attribute
    {
    }
}
