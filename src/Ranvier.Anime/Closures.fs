namespace Ranvier.Anime

open System.Runtime.CompilerServices

/// <summary>Keeps a class's cached function value a field.</summary>
[<RequireQualifiedAccess>]
module internal Closure =
    /// <summary>Returns the function unchanged, so a class <c>let</c> bound to the call is a field holding one closure.</summary>
    /// <remarks>
    /// A class <c>let</c> bound directly to a lambda compiles to a method, and every use of it as a value allocates a new
    /// closure. Not inlined, so the binding stays an application rather than a lambda.
    /// </remarks>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let keep (f: 'T) : 'T = f
