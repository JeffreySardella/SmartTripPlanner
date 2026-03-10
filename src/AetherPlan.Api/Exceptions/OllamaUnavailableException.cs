namespace AetherPlan.Api.Exceptions;

public class OllamaUnavailableException : Exception
{
    public OllamaUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
