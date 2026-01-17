using System;
using Core.Entities;
using Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;

namespace Infrastructure.Services;

public class PaymentService(IConfiguration config, ICartService cartService, IUnitOfWork unitOfWork, ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<ShoppingCart?> CreateOrUpdatePaymentIntent(string cartId)
    {
        StripeConfiguration.ApiKey = config["StripeSettings:SecretKey"];

        var cart = await cartService.GetCartAsync(cartId);

        if (cart == null) return null;

        var shippingPrice = 0m;

        if (cart.DeliveryMethodId.HasValue)
        {
            var deliveryMethod = await unitOfWork.Repository<DeliveryMethod>().GetByIdAsync((int)cart.DeliveryMethodId);

            if (deliveryMethod == null) return null;

            shippingPrice = deliveryMethod.Price;
        }

        foreach (var item in cart.Items)
        {
            var productItem = await unitOfWork.Repository<Core.Entities.Product>().GetByIdAsync(item.ProductId);

            if (productItem == null) return null;

            if (item.Price != productItem.Price)
            {
                item.Price = productItem.Price;
            }
        }

        var service = new PaymentIntentService();
        PaymentIntent? intent = null;

        // Calculate amount with proper rounding to avoid PaymentMismatch errors
        // Total is items subtotal + shipping, all in cents
        var itemsTotal = Math.Round(cart.Items.Sum(x => x.Quantity * x.Price), 2);
        var grandTotal = Math.Round(itemsTotal + shippingPrice, 2);
        var amountInCents = (long)Math.Round(grandTotal * 100, MidpointRounding.AwayFromZero);

        // Add logging for debugging PaymentMismatch issues
        logger.LogInformation($"=== PAYMENT SERVICE DEBUG ===");
        logger.LogInformation($"CartId: {cartId}");
        logger.LogInformation($"Items: {string.Join(", ", cart.Items.Select(x => $"${x.Price} x {x.Quantity}"))}");
        logger.LogInformation($"ItemsTotal: ${itemsTotal}");
        logger.LogInformation($"ShippingPrice: ${shippingPrice}");
        logger.LogInformation($"GrandTotal: ${grandTotal}");
        logger.LogInformation($"AmountInCents: {amountInCents} (${grandTotal})");
        logger.LogInformation($"===========================");

        if (string.IsNullOrEmpty(cart.PaymentIntentId))
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = amountInCents,
                Currency = "usd",
                PaymentMethodTypes = ["card"]
            };
            intent = await service.CreateAsync(options);
            cart.PaymentIntentId = intent.Id;
            cart.ClientSecret = intent.ClientSecret;
        } else
        {
            var options = new PaymentIntentUpdateOptions
            {
                Amount = amountInCents,
            };
            intent = await service.UpdateAsync(cart.PaymentIntentId, options);
            cart.ClientSecret = intent.ClientSecret;
        }

        await cartService.SetCartAsync(cart);

        return cart;

    }
}
