import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CreateSubscription } from '../subscription.model';
import { SubscriptionService } from '../subscription.service';

@Component({
  selector: 'app-subscription-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <form (ngSubmit)="submit()">
      <input [(ngModel)]="model.serviceName" name="serviceName" placeholder="服務名稱" required />
      <input [(ngModel)]="model.cost" name="cost" type="number" placeholder="金額" required />
      <input [(ngModel)]="model.currency" name="currency" placeholder="幣別 (TWD)" required />
      <input [(ngModel)]="model.nextRenewalDate" name="nextRenewalDate" type="date" required />
      <input [(ngModel)]="model.notifyDaysBefore" name="notifyDaysBefore" type="number" />
      <button type="submit">新增</button>
    </form>
  `
})
export class SubscriptionFormComponent {
  @Output() created = new EventEmitter<void>();
  model: CreateSubscription = {
    serviceName: '', cost: 0, currency: 'TWD', cycle: 0,
    nextRenewalDate: '', notifyDaysBefore: 7, channels: 1
  };
  constructor(private svc: SubscriptionService) {}
  submit() {
    this.svc.create(this.model).subscribe(() => this.created.emit());
  }
}
