import { Component, inject, OnInit } from '@angular/core';
import { ShopService } from '../../core/services/shop.service';
import { Product } from '../../shared/models/product';
import { MatCard, MatCardContent } from '@angular/material/card';
import { HeaderComponent } from '../../layout/header/header.component';
import { ProductItemComponent } from './product-item/product-item.component';
import { CurrencyPipe } from '@angular/common';

@Component({
  selector: 'app-shop',
  imports: [ProductItemComponent],
  templateUrl: './shop.component.html',
  styleUrl: './shop.component.scss',
})
export class ShopComponent implements OnInit {
  private shopService = inject(ShopService);

  products: Product[] = [];

  ngOnInit(): void {
    this.shopService.getProducts().subscribe({
      next: (response) => (this.products = response.data),
      error: (error) => console.log(error),
    });
  }
}
