using System;
using Core.Entities;
using Core.Interfaces;
using Core.Specification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace API.Controllers;

public class PaymentsController(IPaymentService paymentService, IUnitOfWork unitOfWork, ILogger<PaymentsController> logger, IConfiguration config) : BaseApiController
{
    private readonly string _whSecret = config["StripeSettings:WhSecret"];

    [Authorize]
    [HttpPost("{cartId}")]
    public async Task<ActionResult<ShoppingCart>> CreateOrUpdatePaymentIntent(string cartId)
    {
        var cart = await paymentService.CreateOrUpdatePaymentIntent(cartId);

        if (cart == null) return BadRequest("Problem with your cart");

        return Ok(cart);
    }

    [HttpGet("delivery-methods")]
    public async Task<ActionResult<IReadOnlyList<DeliveryMethod>>> GetDeliveryMethods()
    {
        return Ok(await unitOfWork.Repository<DeliveryMethod>().ListAllAsync());
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();

        try
        {
            var stripeEvent = ConstructStripeEvent(json);

            if (stripeEvent.Data.Object is not PaymentIntent intent)
            {
                return BadRequest("Invalid event data");
            }

            await HandlePaymentIntentSucceeded(intent);

            return Ok();
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe webhook error");
            return StatusCode(StatusCodes.Status500InternalServerError,  "Webhook error");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe webhook error");
            // Returning ex.Message allows you to see the real error in your Stripe CLI logs
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message); 
        }
    }

    private Event ConstructStripeEvent(string json)
    {
        try
        {
            return EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], 
                _whSecret);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to construct stripe event");
            throw new StripeException("Invalid signature");
        }
    }

    private async Task HandlePaymentIntentSucceeded(PaymentIntent intent)
    {
        if (intent.Status == "succeeded") 
        {
            var spec = new OrderSpecification(intent.Id, true);
            var order = await unitOfWork.Repository<Order>().GetEntityWithSpec(spec);

            // If order not found, it means the order hasn't been created yet in the database
            // This can happen due to race condition between webhook and client-side order creation
            // Log this for monitoring, but don't throw - the order will be created shortly
            if (order == null)
            {
                logger.LogWarning($"Order not yet found for PaymentIntentId: {intent.Id}. " +
                    "This is likely due to async processing - order will be created shortly by client request.");
                return;
            }

            var orderTotalInCents = (long)Math.Round(order.GetTotal() * 100, 
                MidpointRounding.AwayFromZero);

            logger.LogInformation($"Payment verification for OrderId: {order.Id}, PaymentIntentId: {intent.Id} | " +
                $"Order Total in Cents: {orderTotalInCents} | Stripe Amount in Cents: {intent.Amount}");

            if (orderTotalInCents != intent.Amount)
            {
                logger.LogWarning($"Payment mismatch detected! OrderId: {order.Id}, " +
                    $"Expected: {intent.Amount} cents, Got: {orderTotalInCents} cents (Difference: {Math.Abs(intent.Amount - orderTotalInCents)} cents)");
                order.Status = OrderStatus.PaymentMismatch;
            } 
            else
            {
                order.Status = OrderStatus.PaymentReceived;
            }

            await unitOfWork.Complete();

            //TODO: SignalR implementation
        }
    }

}