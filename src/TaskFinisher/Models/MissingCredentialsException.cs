namespace TaskFinisher.Models;

/// <summary>
/// Thrown when required credentials are absent in a non-interactive context.
/// Treated as a clean exit (code 0) rather than an application error.
/// </summary>
public sealed class MissingCredentialsException(string message) : Exception(message);
