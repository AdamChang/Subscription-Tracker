using FluentAssertions;
using FluentValidation.TestHelper;
using SubscriptionTracker.Api.Contracts;
using SubscriptionTracker.Api.Validation;
using SubscriptionTracker.Contracts;
using Xunit;

namespace SubscriptionTracker.Api.Tests.Validation;

public class CreateSubscriptionRequestValidatorTests
{
    private readonly CreateSubscriptionRequestValidator _validator = new();

    private static CreateSubscriptionRequest Valid() =>
        new("Netflix", 390m, "TWD", BillingCycle.Monthly,
            new DateOnly(2026, 7, 1), 7, NotifyChannel.Discord);

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_service_name_fails()
    {
        _validator.TestValidate(Valid() with { ServiceName = "" })
            .ShouldHaveValidationErrorFor(x => x.ServiceName);
    }

    [Fact]
    public void Non_positive_cost_fails()
    {
        _validator.TestValidate(Valid() with { Cost = 0m })
            .ShouldHaveValidationErrorFor(x => x.Cost);
    }

    [Fact]
    public void Bad_currency_length_fails()
    {
        _validator.TestValidate(Valid() with { Currency = "TW" })
            .ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Out_of_range_notify_days_fails()
    {
        _validator.TestValidate(Valid() with { NotifyDaysBefore = 100 })
            .ShouldHaveValidationErrorFor(x => x.NotifyDaysBefore);
    }
}
