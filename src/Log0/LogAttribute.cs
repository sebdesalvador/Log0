using Microsoft.Extensions.Logging;

namespace Log0;

/// <summary>
/// Logs calls to a method: <c>Start</c> on entry, then <c>Success</c>, <c>Canceled</c> (warning)
/// or <c>Error</c> (error, with the exception). Parameter values are added to the <c>Start</c>
/// entry when <see cref="LogLevel.Debug"/> or lower is enabled.
/// </summary>
/// <remarks>
/// Every direct call to the method in the project is intercepted at compile time and logged; on
/// an interface method, so is every call through the interface. The method itself is unchanged:
/// <code>
/// [Log]
/// public Task&lt;Order&gt; GetOrder( int id ) =&gt; _repository.Find( id );
/// </code>
/// </remarks>
[ AttributeUsage( AttributeTargets.Method, Inherited = false ) ]
public sealed class LogAttribute : Attribute
{
    #region Properties

    /// <summary>The level of the <c>Start</c> and <c>Success</c> entries.</summary>
    public LogLevel Level { get; }

    #endregion

    #region Constructors

    /// <summary>Logs at <see cref="LogLevel.Information"/>.</summary>
    public LogAttribute() : this( LogLevel.Information ) { }

    /// <summary>Logs <c>Start</c> and <c>Success</c> at <paramref name="level"/>.</summary>
    /// <param name="level">
    /// The level of the <c>Start</c> and <c>Success</c> entries. <c>Canceled</c> is always a
    /// warning and <c>Error</c> always an error.
    /// </param>
    public LogAttribute( LogLevel level ) => Level = level;

    #endregion
}
