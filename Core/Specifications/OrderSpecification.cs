using System;
using Core.Entities;
using Core.Specifications;

namespace Core.Specification;

public class OrderSpecification : BaseSpecification<Order>
{
    public OrderSpecification(string email) : base(x => x.BuyerEmail == email)
    {
        AddInclude(x => x.OrderItems);
        AddInclude(x => x.DeliveryMethod);
        AddOrderByDescending(x => x.OrderDate);
    }

    public OrderSpecification(string email, int id) : base(x => x.BuyerEmail == email && x.Id == id)
    {
        AddInclude("OrderItems");
        AddInclude("DeliveryMethod");
    }

    public OrderSpecification(string paymentIntentId, bool isPaymentIntent) : 
    base(x => x.PaymentIntentId == paymentIntentId)
    {
        // Use the expression-based AddInclude
        AddInclude(x => x.OrderItems);
        AddInclude(x => x.DeliveryMethod);
    }
}