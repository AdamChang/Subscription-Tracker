import { Component } from '@angular/core';
import { SubscriptionListComponent } from './subscription-list/subscription-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [SubscriptionListComponent],
  template: `<app-subscription-list></app-subscription-list>`
})
export class AppComponent {}
