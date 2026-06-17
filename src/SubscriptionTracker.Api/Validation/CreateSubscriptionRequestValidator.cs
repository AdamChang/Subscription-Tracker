using FluentValidation;
using SubscriptionTracker.Api.Contracts;

namespace SubscriptionTracker.Api.Validation;

public class CreateSubscriptionRequestValidator : AbstractValidator<CreateSubscriptionRequest>
{
    public CreateSubscriptionRequestValidator()
    {
        RuleFor(x => x.ServiceName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Cost).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3);
        RuleFor(x => x.NotifyDaysBefore).InclusiveBetween(0, 90);
    }
}
