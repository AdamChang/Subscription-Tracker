using FluentValidation;
using SubscriptionTracker.Api.Endpoints;
using SubscriptionTracker.Api.State;
using SubscriptionTracker.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddSingleton<ISubscriptionStore, DaprSubscriptionStore>();
builder.Services.AddScoped<IValidator<SubscriptionTracker.Api.Contracts.CreateSubscriptionRequest>,
    CreateSubscriptionRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// 全域例外處理（不在各 method 寫 try-catch）
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapSubscriptions();
app.MapStats();
app.MapJobs();

app.Run();
