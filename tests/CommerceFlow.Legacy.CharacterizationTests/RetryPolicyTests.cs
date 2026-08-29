using CommerceFlow.Legacy.DAL;

namespace CommerceFlow.Legacy.CharacterizationTests;

// Deterministic unit tests for RetryPolicy's generic retry mechanics -- attempt counting,
// backoff invocation, give-up behavior. These do NOT exercise a real database, a real deadlock,
// or SqlException specifically: they prove the retry LOOP is correct, not that
// SqlException.Number == 1205 is the right thing to retry on. That one-line mapping lives in
// OrderDataAccess.CreateOrder and is verified by inspection there, not by a test here.
public class RetryPolicyTests
{
    private class RetryableException : Exception;

    private class NonRetryableException : Exception;

    [Fact]
    public void Execute_SucceedsWithinBudget_ReturnsResultAndStopsRetrying()
    {
        var operationCalls = 0;
        var backoffCalls = 0;

        var result = RetryPolicy.Execute(
            operation: () =>
            {
                operationCalls++;
                if (operationCalls < 3)
                {
                    throw new RetryableException();
                }

                return 42;
            },
            isRetryable: ex => ex is RetryableException,
            maxAttempts: 3,
            backoff: _ => backoffCalls++);

        Assert.Equal(42, result);
        Assert.Equal(3, operationCalls); // failed twice, succeeded on the 3rd (of 3 total)
        Assert.Equal(2, backoffCalls);   // backoff runs only between attempts, never after success
    }

    [Fact]
    public void Execute_ExhaustsRetryBudget_ThrowsOriginalExceptionAfterExactlyMaxAttempts()
    {
        var operationCalls = 0;
        var backoffCalls = 0;
        var thrown = new RetryableException();

        var caught = Assert.Throws<RetryableException>(() => RetryPolicy.Execute<int>(
            operation: () =>
            {
                operationCalls++;
                throw thrown;
            },
            isRetryable: ex => ex is RetryableException,
            maxAttempts: 3,
            backoff: _ => backoffCalls++));

        Assert.Same(thrown, caught);     // the original exception instance, not a translated one
        Assert.Equal(3, operationCalls); // exactly maxAttempts total calls, not maxAttempts + 1
        Assert.Equal(2, backoffCalls);   // backoff runs between attempts: maxAttempts - 1 times
    }

    [Fact]
    public void Execute_NonRetryableException_IsNotRetried()
    {
        var operationCalls = 0;
        var backoffCalls = 0;
        var thrown = new NonRetryableException();

        var caught = Assert.Throws<NonRetryableException>(() => RetryPolicy.Execute<int>(
            operation: () =>
            {
                operationCalls++;
                throw thrown;
            },
            isRetryable: ex => ex is RetryableException, // NonRetryableException never matches
            maxAttempts: 3,
            backoff: _ => backoffCalls++));

        Assert.Same(thrown, caught);
        Assert.Equal(1, operationCalls); // no retry at all
        Assert.Equal(0, backoffCalls);
    }
}
