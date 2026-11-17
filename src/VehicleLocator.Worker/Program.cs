using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VehicleLocator.Application.Positions;
using VehicleLocator.Application.Vehicles;
using VehicleLocator.Worker.Consumers;
using VehicleLocator.Worker.Support.Kafka;
using VehicleLocator.Worker.Support.Replay;
using VehicleLocator.Worker.Support.Valkey;

var builder = Host.CreateApplicationBuilder(args);

var replay = builder.Configuration.GetSection("Replay").Get<ReplayOptions>() ?? new ReplayOptions();

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.PostConfigure<KafkaOptions>(kafka =>
    ReplayConfiguration.ApplyGroupSuffix(kafka, replay, DateTimeOffset.UtcNow));
builder.Services.Configure<ValkeyOptions>(builder.Configuration.GetSection("Valkey"));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var valkey = sp.GetRequiredService<IOptions<ValkeyOptions>>().Value;
    ReplayConfiguration.Validate(replay, valkey);

    var config = ConfigurationOptions.Parse(valkey.ConnectionString);
    config.DefaultDatabase = valkey.Database;
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
builder.Services.AddSingleton<IConsumerLoop, ConsumerLoop>();

builder.Services.AddSingleton<VehicleViewWriter>();
builder.Services.AddSingleton<ApplyVehicleChangeUseCase>();
builder.Services.AddSingleton<PositionViewWriter>();
builder.Services.AddSingleton<ApplyPositionChangeUseCase>();

builder.Services.AddHostedService<VehicleConsumer>();
builder.Services.AddHostedService<PositionConsumer>();

builder.Build().Run();