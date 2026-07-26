#if NETFRAMEWORK
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// .NET Framework에는 없는 타입. record / init 접근자를 net48에서 쓰기 위한 셔틀.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
#endif
