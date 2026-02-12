using System;
using API.Extensions;
using API.SignalR;
using Core.Entities;
using Core.Interfaces;
using Core.Specification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Stripe;

namespace API.Controllers;

public class PaymentsController(IPaymentService paymentService, IUnitOfWork unitOfWork, ILogger<PaymentsController> logger, IConfiguration config, IHubContext<NotificationHub> hubContext) : BaseApiController
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
    [AllowAnonymous]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();

        try
        {
            var stripeEvent = ConstructStripeEvent(json);
            
            logger.LogInformation($"Webhook received: Event type = {stripeEvent.Type}, Data type = {stripeEvent.Data.Object?.GetType().Name}");

            if (stripeEvent.Data.Object is not PaymentIntent intent)
            {
                logger.LogWarning($"Webhook event data is not PaymentIntent. Type: {stripeEvent.Data.Object?.GetType().Name}");
                return BadRequest("Invalid event data");
            }

            logger.LogInformation($"Processing PaymentIntent: {intent.Id}, Status: {intent.Status}");
            
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
        logger.LogInformation($"HandlePaymentIntentSucceeded called. Status: {intent.Status}, Amount: {intent.Amount}");
        
        if (intent.Status != "succeeded") 
        {
            logger.LogInformation($"PaymentIntent status is '{intent.Status}', not 'succeeded'. Skipping processing.");
            return;
        }

        var spec = new OrderSpecification(intent.Id, true);
        var order = await unitOfWork.Repository<Order>().GetEntityWithSpec(spec);

        if (order == null)
        {
            logger.LogWarning($"Order not yet found for PaymentIntentId: {intent.Id}. " +
                "This is likely due to async processing - order will be created shortly by client request.");
            return;
        }

        logger.LogInformation($"Found order for PaymentIntentId: {intent.Id}. OrderId: {order.Id}, Current Status: {order.Status}");

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
            logger.LogInformation($"Payment amount matches. Updating order status to PaymentReceived.");
            order.Status = OrderStatus.PaymentReceived;
        }

        await unitOfWork.Complete();
        logger.LogInformation($"Order status updated. OrderId: {order.Id}, New Status: {order.Status}");

        var connectionId = NotificationHub.GetConnectionIdByEmail(order.BuyerEmail);

        if (!string.IsNullOrEmpty(connectionId))
        {
            logger.LogInformation($"Sending SignalR notification to user {order.BuyerEmail} (ConnectionId: {connectionId})");
            await hubContext.Clients.Client(connectionId)
                .SendAsync("OrderCompleteNotification", order.ToDto());
        }
        else
        {
            logger.LogWarning($"No SignalR connection found for user {order.BuyerEmail}. Notification not sent, but order status has been updated.");
        }
    }

}