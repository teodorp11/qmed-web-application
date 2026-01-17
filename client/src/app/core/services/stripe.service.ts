import { inject, Injectable } from '@angular/core';
import {
  ConfirmationToken,
  loadStripe,
  Stripe,
  StripeAddressElement,
  StripeAddressElementOptions,
  StripeElements,
  StripePaymentElement,
} from '@stripe/stripe-js';
import { environment } from '../../../environments/environment';
import { CartService } from './cart.service';
import { HttpClient } from '@angular/common/http';
import { Cart } from '../../shared/models/cart';
import { firstValueFrom, map } from 'rxjs';
import { AccountService } from './account.service';

@Injectable({
  providedIn: 'root',
})
export class StripeService {
  baseUrl = environment.apiUrl;
  private cartService = inject(CartService);
  private accountService = inject(AccountService);
  private http = inject(HttpClient);
  private stripePromise: Promise<Stripe | null>;
  private elements?: StripeElements;
  private addressElement?: StripeAddressElement;
  private paymentElement?: StripePaymentElement;

  constructor() {
    this.stripePromise = loadStripe(environment.stripePublicKey);
  }

  getStripeInstance() {
    return this.stripePromise;
  }

  async initializeElements() {
    if (!this.elements) {
      const stripe = await this.getStripeInstance();
      if (stripe) {
        try {
          // Get or create payment intent
          const updatedCart = await firstValueFrom(this.createOrUpdatePaymentIntent());
          if (!updatedCart || !updatedCart.clientSecret) {
            throw new Error('Failed to create payment intent');
          }
          this.elements = stripe.elements({
            clientSecret: updatedCart.clientSecret,
            appearance: { labels: 'floating' },
          });
        } catch (error) {
          // Reset elements so next attempt can retry
          this.elements = undefined;
          throw error;
        }
      } else {
        throw new Error('Stripe has not been loaded.');
      }
    }
    return this.elements;
  }

  async initializeAddressElements() {
    // Create address elements without payment intent
    const stripe = await this.getStripeInstance();
    if (!stripe) {
      throw new Error('Stripe has not been loaded.');
    }
    // Create a temporary elements instance just for the address element
    // This doesn't require a clientSecret
    const tempElements = stripe.elements({
      appearance: { labels: 'floating' },
    });
    return tempElements;
  }

  async createPaymentElement() {
    if (!this.paymentElement) {
      const elements = await this.initializeElements();
      if (elements) {
        this.paymentElement = elements.create('payment');
      } else {
        throw new Error('Elements instance has not been initialized.');
      }
    }
    return this.paymentElement;
  }

  async createAddressElement() {
    if (!this.addressElement) {
      const elements = await this.initializeAddressElements();
      if (elements) {
        const user = this.accountService.currentUser();
        let defaultValues: StripeAddressElementOptions['defaultValues'] = {};

        if (user) {
          defaultValues.name = user.firstName + ' ' + user.lastName;
        }

        if (user?.address) {
          defaultValues.address = {
            line1: user.address.line1,
            line2: user.address.line2,
            city: user.address.city,
            state: user.address.state,
            country: user.address.country,
            postal_code: user.address.postalCode,
          };
        }
        const options: StripeAddressElementOptions = {
          mode: 'shipping',
          defaultValues,
        };
        this.addressElement = elements.create('address', options);
      } else {
        throw new Error('Elements instance has not been loaded');
      }
    }
    return this.addressElement;
  }

  async createConfirmationToken() {
    const stripe = await this.getStripeInstance();
    const elements = await this.initializeElements();
    const result = await elements.submit();
    if (result.error) throw new Error(result.error.message);
    if (stripe) {
      return await stripe.createConfirmationToken({ elements });
    } else {
      throw new Error('Stripe not available');
    }
  }

  async confirmPayment(confirmationToken: ConfirmationToken) {
    console.log('confirmPayment called with token:', confirmationToken.id);
    const stripe = await this.getStripeInstance();
    const elements = await this.initializeElements();
    const result = await elements.submit();
    if (result.error) throw new Error(result.error.message);

    const cart = this.cartService.cart();
    const clientSecret = cart?.clientSecret;

    console.log('About to call stripe.confirmPayment with clientSecret:', clientSecret);

    if (!stripe) {
      throw new Error('Stripe library failed to load');
    }

    if (!clientSecret) {
      throw new Error(
        'Payment intent was not properly initialized. Please refresh the page and try again.',
      );
    }

    const paymentResult = await stripe.confirmPayment({
      clientSecret: clientSecret,
      confirmParams: {
        confirmation_token: confirmationToken.id,
      },
      redirect: 'if_required',
    });

    console.log('Payment result:', paymentResult);
    return paymentResult;
  }

  createOrUpdatePaymentIntent() {
    const cart = this.cartService.cart();

    if (!cart || !cart.id) {
      throw new Error('Cart is not loaded. Please refresh the page.');
    }

    console.log('Creating/updating payment intent for cart:', cart.id);
    return this.http.post<Cart>(this.baseUrl + 'payments/' + cart.id, {}).pipe(
      map((cart) => {
        console.log(
          'Payment intent created/updated successfully. clientSecret:',
          cart.clientSecret,
          'paymentIntentId:',
          cart.paymentIntentId,
        );
        this.cartService.setCart(cart);
        return cart;
      }),
    );
  }

  disposeElements() {
    this.elements = undefined;
    this.addressElement = undefined;
    this.paymentElement = undefined;
  }
}
