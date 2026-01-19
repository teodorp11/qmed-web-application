import { Pipe, PipeTransform } from '@angular/core';
import { ConfirmationToken } from '@stripe/stripe-js';
import { PaymentSummary } from '../models/order';

@Pipe({
  name: 'paymentCard',
  standalone: true,
})
export class PaymentCardPipe implements PipeTransform {
  transform(
    value?: ConfirmationToken['payment_method_preview'] | PaymentSummary,
    ...args: unknown[]
  ): unknown {
    if (!value) {
      return 'Unknown payment method';
    }

    // Handle PaymentSummary object (from checkout-success)
    if ('brand' in value && 'last4' in value) {
      const summary = value as PaymentSummary;
      return `${summary.brand.toUpperCase()} **** **** **** ${summary.last4}, Exp: ${summary.expMonth}/${summary.expYear}`;
    }

    // Handle ConfirmationToken payment_method_preview (from checkout)
    if ('card' in value) {
      const { brand, last4, exp_month, exp_year } = (
        value as ConfirmationToken['payment_method_preview']
      ).card!;
      return `${brand.toUpperCase()} **** **** **** ${last4}, Exp: ${exp_month}/${exp_year}`;
    }

    return 'Unknown payment method';
  }
}
