using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VehicleLocator.Application.Vehicles;
using VehicleLocator.Worker.Consumers;
using VehicleLocator.Worker.Support.Kafka;
using VehicleLocator.Worker.Support.Valkey;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<ValkeyOptions>(builder.Configuration.GetSection("Valkey"));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<ValkeyOptions>>().Value.ConnectionString));

builder.Services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
builder.Services.AddSingleton<IConsumerLoop, ConsumerLoop>();
builder.Services.AddSingleton<VehicleViewWriter>();
builder.Services.AddSingleton<ApplyVehicleChangeUseCase>();
builder.Services.AddHostedService<VehicleConsumer>();

builder.Build().Run();
