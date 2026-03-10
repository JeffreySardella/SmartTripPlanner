namespace AetherPlan.Api.Exceptions;

public class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
