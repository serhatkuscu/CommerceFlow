namespace CommerceFlow.Legacy.Models;

// Bare int status codes, not a typed enum -- matches how the schema stores OrderStatus
// (CK_Orders_OrderStatus CHECK (OrderStatus IN (0,1,2))). Only Pending is ever written by
// the application; Confirmed/Cancelled are reserved values reachable only by a direct,
// out-of-band database update.
public static class OrderStatus
{
    public const int Pending = 0;
    public const int Confirmed = 1;
    public const int Cancelled = 2;
}
