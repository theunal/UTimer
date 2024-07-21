using System.Runtime.Serialization;

namespace UTimer.Cronos;

/// <summary>
/// Represents an exception that's thrown, when invalid Cron expression is given.
/// </summary>
#if !NETSTANDARD1_0
[Serializable]
#endif
public class CronFormatException : FormatException
{
    private const string BaseMessage = "The given cron expression has an invalid format.";

    /// <summary>
    /// Initializes a new instance of the <see cref="CronFormatException"/> class.
    /// </summary>
    public CronFormatException() : this(BaseMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronFormatException"/> class with
    /// a specified error message.
    /// </summary>
    public CronFormatException(string message) : this(message, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronFormatException"/> class with
    /// a specified error message and a reference to the inner exception that is the
    /// cause of this exception.
    /// </summary>
    public CronFormatException(string message, Exception innerException)
        : base($"{BaseMessage} {message}", innerException)
    {
    }

    internal CronFormatException(CronField field, string message) : this($"{BaseMessage} {field}: {message}")
    {
    }

#if !NETSTANDARD1_0
    /// <inheritdoc />
    protected CronFormatException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
#endif
}