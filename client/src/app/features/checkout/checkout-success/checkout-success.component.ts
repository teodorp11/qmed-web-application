import { Component, effect, inject, OnDestroy, OnInit } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { SignalrService } from '../../../core/services/signalr.service';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CurrencyPipe, DatePipe, NgIf } from '@angular/common';
import { OrderService } from '../../../core/services/order.service';
import { AddressPipe } from '../../../shared/pipes/address-pipe';
import { PaymentCardPipe } from '../../../shared/pipes/payment-card-pipe';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-checkout-success',
  standalone: true,
  imports: [
    MatButton,
    RouterLink,
    MatProgressSpinnerModule,
    DatePipe,
    CurrencyPipe,
    PaymentCardPipe,
    NgIf,
    AddressPipe,
  ],
  templateUrl: './checkout-success.component.html',
  styleUrl: './checkout-success.component.scss',
})
export class CheckoutSuccessComponent implements OnInit, OnDestroy {
  signalrService = inject(SignalrService);
  private orderService = inject(OrderService);
  private pollInterval: any;

  constructor() {
    // Debug: Log the order signal to check paymentSummary
    effect(() => {
      const order = this.signalrService.orderSignal();
      if (order) {
        console.log('Order received via SignalR:', order);
        console.log('Payment Summary:', order.paymentSummary);
        this.stopPolling();
      }
    });
  }

  async ngOnInit() {
    // If no order received via SignalR yet, start polling for orders
    // This fixes the race condition where webhook runs before order is created
    if (!this.signalrService.orderSignal()) {
      await this.pollForLatestOrder();
    }
  }

  private async pollForLatestOrder() {
    let attempts = 0;
    const maxAttempts = 30; // Poll for up to 30 seconds (1 second intervals)

    this.pollInterval = setInterval(async () => {
      attempts++;
      try {
        const orders = await firstValueFrom(this.orderService.getOrdersForUser());

        if (orders && orders.length > 0) {
          // Get the most recent order (should be the one just created)
          const latestOrder = orders[0];

          console.log('Fetched latest order:', latestOrder);

          // Set the signal to display the order
          this.signalrService.orderSignal.set(latestOrder);
          this.stopPolling();

          return;
        }
      } catch (error) {
        console.error('Error fetching orders:', error);
      }

      // Stop polling after max attempts
      if (attempts >= maxAttempts) {
        console.warn('Max polling attempts reached. Order may still be processing.');
        this.stopPolling();
      }
    }, 1000);
  }

  private stopPolling() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.orderService.orderComplete = false;
    this.signalrService.orderSignal.set(null);
  }
}
