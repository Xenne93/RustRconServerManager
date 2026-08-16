namespace RustRconServerManager.Backend.Helpers
{
    /// <summary>
    /// Strips CR/LF characters from user-controlled string values before they reach a logger,
    /// so an attacker cannot inject fake newline-delimited log entries (log forging / CWE-117).
    /// Structured logging placeholders alone do not prevent this: the substituted value is still
    /// rendered verbatim into the formatted text line by the default log providers.
    /// </summary>
    public static class LogSanitizer
    {
        public static string? Sanitize(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            return input.Replace("\r", "").Replace("\n", "");
        }
    }
}
