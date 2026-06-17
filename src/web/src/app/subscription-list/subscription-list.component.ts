import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Subscription } from '../subscription.model';
import { SubscriptionService } from '../subscription.service';
import { SubscriptionFormComponent } from '../subscription-form/subscription-form.component';

@Component({
  selector: 'app-subscription-list',
  standalone: true,
  imports: [CommonModule, SubscriptionFormComponent],
  template: `
    <h2>我的訂閱</h2>
    <app-subscription-form (created)="load()"></app-subscription-form>
    <table>
      <tr><th>服務</th><th>金額</th><th>下次續費</th><th></th></tr>
      <tr *ngFor="let s of subs">
        <td>{{ s.serviceName }}</td>
        <td>{{ s.cost }} {{ s.currency }}</td>
        <td>{{ s.nextRenewalDate }}</td>
        <td><button (click)="remove(s.id)">刪除</button></td>
      </tr>
    </table>
  `
})
export class SubscriptionListComponent implements OnInit {
  subs: Subscription[] = [];
  constructor(private svc: SubscriptionService) {}
  ngOnInit() { this.load(); }
  load() { this.svc.list().subscribe(s => this.subs = s); }
  remove(id: string) { this.svc.remove(id).subscribe(() => this.load()); }
}
