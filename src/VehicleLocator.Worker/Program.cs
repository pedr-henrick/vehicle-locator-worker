using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VehicleLocator.Application.Positions;
using VehicleLocator.Application.Vehicles;
using VehicleLocator.Worker;
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
builder.Services.AddSingleton<PositionViewWriter>();
builder.Services.AddSingleton<ApplyPositionChangeUseCase>();

builder.Services.AddHostedService<VehicleConsumer>();
builder.Services.AddHostedService<PositionConsumer>();

builder.Build().Run();
