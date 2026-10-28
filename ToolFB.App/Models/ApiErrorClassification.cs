namespace ToolFB.App.Models;

public static class ApiErrorClassification
{
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string ActionBlocked = "ACTION_BLOCKED";
    public const string RateLimit = "RATE_LIMIT";
    public const string PostNotFound = "POST_NOT_FOUND";
    public const string TargetUnavailable = "TARGET_UNAVAILABLE";
    public const string NetworkError = "NETWORK_ERROR";
    public const string UnknownError = "UNKNOWN_ERROR";

    public static string Classify(int? code, string? type, string? message, int httpStatus, int? subcode)
    {
        message ??= string.Empty;
        type ??= string.Empty;

        // Subcode checks
        if (subcode.HasValue)
        {
            switch (subcode.Value)
            {
                case 458:
                case 459:
                case 460:
                case 463:
                case 467:
                    return TokenInvalid;
                case 1752129:
                case 1404078:
                case 1404006:
                case 1404106:
                    return ActionBlocked;
            }
        }

        // Code checks
        if (code.HasValue)
        {
            switch (code.Value)
            {
                case 190: // Invalid OAuth access token
                case 102: // Session expired
                case 104:
                    return TokenInvalid;

                case 368: // Temporarily blocked from performing action
                case 17:  // User request limit reached
                case 341: // Action limit
                case 200: // Permission error
                case 506: // Duplicate post
                    return ActionBlocked;

                case 1:   // API unknown
                case 2:   // Service temporarily unavailable
                case 4:   // Application request limit reached
                case 32:  // Page request limit reached
                case 613: // Calls to stream have exceeded rate of 600 calls per 600 seconds
                    return RateLimit;

                case 100: // Invalid parameter / Unsupported post request
                case 803: // Some of the aliases you requested do not exist
                    if (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("unsupported post request", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        return PostNotFound;
                    }
                    return ActionBlocked;
            }
        }

        // Message heuristic
        var msgLower = message.ToLowerInvariant();
        if (msgLower.Contains("session has been invalidated") ||
            msgLower.Contains("malformed access token") ||
            msgLower.Contains("error validating access token") ||
            msgLower.Contains("user is checkpointed"))
        {
            return TokenInvalid;
        }

        if (msgLower.Contains("action blocked") ||
            msgLower.Contains("temporarily blocked") ||
            msgLower.Contains("spam") ||
            msgLower.Contains("restricted"))
        {
            return ActionBlocked;
        }

        if (msgLower.Contains("rate limit") || msgLower.Contains("too many requests"))
        {
            return RateLimit;
        }

        if (httpStatus == 401 || httpStatus == 403)
        {
            return TokenInvalid;
        }

        if (httpStatus == 429)
        {
            return RateLimit;
        }

        return UnknownError;
    }
}
