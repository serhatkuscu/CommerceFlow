namespace CommerceFlow.Legacy.DAL;

// Generic bounded-retry-with-backoff loop, deliberately decoupled from SqlException: extracted
// out of OrderDataAccess.CreateOrder so the retry MECHANICS (attempt counting, backoff timing,
// give-up behavior) can be tested deterministically without a real database or a real deadlock.
// It does not know what SqlException.Number == 1205 means -- OrderDataAccess supplies that as
// the isRetryable predicate. Proving "1205 is retryable" is OrderDataAccess's job, not this
// class's; see RetryPolicyTests.cs for exactly what is and isn't covered here.
//
// maxAttempts counts TOTAL calls to operation, not retries beyond the first (maxAttempts: 3
// means at most 3 calls total, 2 of them retries) -- matches OrderDataAccess's existing
// MaxAttempts semantics exactly, so this refactor changes nothing observable.
public static class RetryPolicy
{
    public static T Execute<T>(
        Func<T> operation,
        Func<Exception, bool> isRetryable,
        int maxAttempts,
        Action<int> backoff)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (isRetryable(ex) && attempt < maxAttempts)
            {
                backoff(attempt);
            }
        }
    }
}
