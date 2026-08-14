namespace RustRconServerManager.Backend.Helpers
{
    /// <summary>
    /// Controls whether detailed exception messages are returned in API error responses.
    /// Configured via AppSettings:ShowDetailedErrors in appsettings.json.
    /// </summary>
    public static class ApiErrorHelper
    {
        private static bool _showDetailedErrors;

        public static void Configure(bool showDetailedErrors)
        {
            _showDetailedErrors = showDetailedErrors;
        }

        /// <summary>
        /// Returns the error message for API responses.
        /// When ShowDetailedErrors is true, includes ex.Message with context.
        /// When false, returns only the generic context message.
        /// </summary>
        public static string FormatError(string context, Exception ex)
        {
            return _showDetailedErrors ? $"{context}: {ex.Message}" : context;
        }

        /// <summary>
        /// Returns the error message for API responses without context.
        /// When ShowDetailedErrors is true, returns ex.Message.
        /// When false, returns a generic error message.
        /// </summary>
        public static string FormatError(Exception ex)
        {
            return _showDetailedErrors ? ex.Message : "An unexpected error occurred.";
        }
    }
}
