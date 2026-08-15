namespace CommerceFlow.Legacy.Models;

// Thrown by the DAL after translating a known business-rule SqlException (error numbers
// 51000-51004, see usp_CreateOrder) into something BLL/Web can handle without ever
// inspecting raw SQL exception text.
public class BusinessRuleException : Exception
{
    public int ErrorCode { get; }

    public BusinessRuleException(int errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
