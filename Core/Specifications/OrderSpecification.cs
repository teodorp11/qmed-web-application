using System;
using Core.Entities;
using Core.Specifications;

namespace Core.Specification;

public class OrderSpecification : BaseSpecification<Order>
{
    public OrderSpecification(string email) : base(x => x.BuyerEmail == email)
    {
    }

    public OrderSpecification(string email, int id) : base(x => x.BuyerEmail == email && x.Id == id)
    {
    }
}
