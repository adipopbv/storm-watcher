var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("timescale/timescaledb", "latest-pg16")
    .WithDataVolume();

var stormwatcherDb = postgres.AddDatabase("stormwatcher");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin();

var ingestionWorker = builder.AddProject<Projects.StormWatcher_Ingestion_WorkerHost>("ingestion-worker")
    .WithReference(stormwatcherDb)
    .WithReference(rabbitmq)
    .WaitFor(stormwatcherDb)
    .WaitFor(rabbitmq);

var ingestionLocalScheduler = builder.AddProject<Projects.StormWatcher_Ingestion_LocalSchedulerHost>("ingestion-local-scheduler")
    .WithReference(stormwatcherDb)
    .WithReference(rabbitmq)
    .WaitFor(stormwatcherDb)
    .WaitFor(rabbitmq);

var detection = builder.AddProject<Projects.StormWatcher_Detection_Host>("detection")
    .WithReference(stormwatcherDb)
    .WithReference(rabbitmq)
    .WaitFor(stormwatcherDb)
    .WaitFor(rabbitmq);

var locationCatalog = builder.AddProject<Projects.StormWatcher_LocationCatalog_Host>("location-catalog")
    .WithReference(stormwatcherDb)
    .WithReference(rabbitmq)
    .WaitFor(stormwatcherDb)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.StormWatcher_Notification_Host>("notification")
    .WithReference(stormwatcherDb)
    .WithReference(rabbitmq)
    .WaitFor(stormwatcherDb)
    .WaitFor(rabbitmq)
    .WithReference(locationCatalog); // sync LocationId -> ntfy topic lookup, §3.4

builder.Build().Run();
