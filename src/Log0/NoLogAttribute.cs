namespace Log0;

/// <summary>
/// Keeps a value out of the logs. The generated code writes <c>***</c> instead and never reads
/// the value.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>On a parameter: that parameter is masked.</item>
/// <item>On a type: every parameter of that type is masked, in every logged method.</item>
/// <item>On a <see cref="LogAttribute">[Log]</see> method: no parameter is logged at all.</item>
/// </list>
/// </remarks>
[ AttributeUsage(
    AttributeTargets.Parameter
  | AttributeTargets.Method
  | AttributeTargets.Class
  | AttributeTargets.Struct
  | AttributeTargets.Interface,
    Inherited = false
) ]
public sealed class NoLogAttribute : Attribute;
