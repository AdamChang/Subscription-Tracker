export interface Subscription {
  id: string;
  serviceName: string;
  cost: number;
  currency: string;
  cycle: number;            // 0 Monthly, 1 Yearly
  nextRenewalDate: string;  // yyyy-MM-dd
  notifyDaysBefore: number;
  channels: number;         // 1 Discord, 2 Email, 3 both
  lastNotifiedOn: string | null;
}

export type CreateSubscription = Omit<Subscription, 'id' | 'lastNotifiedOn'>;
