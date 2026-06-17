import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateSubscription, Subscription } from './subscription.model';

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private readonly base = 'http://localhost:5000/subscriptions';
  constructor(private http: HttpClient) {}

  list(): Observable<Subscription[]> {
    return this.http.get<Subscription[]>(this.base);
  }
  create(req: CreateSubscription): Observable<Subscription> {
    return this.http.post<Subscription>(this.base, req);
  }
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
