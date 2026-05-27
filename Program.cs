using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Telegram.Bot;
using VocabifyBot.Interfaces;
using VocabifyBot.Services;
using VocabifyBot.Services.Handlers;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .Build();

var botToken = config["BotToken"]!;
var claudeApiKey = config["ClaudeApiKey"]!;
var mongoUri = config["MongoDb:ConnectionString"]!;
var mongoDbName = config["MongoDb:DatabaseName"] ?? "vocabifybot";
var allowedTeachers = (config.GetSection("AllowedTeachers").Get<string[]>() ?? [])
    .Select(u => u.TrimStart('@').ToLowerInvariant())
    .ToHashSet();

var services = new ServiceCollection();
services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
services.AddSingleton<IMongoDatabase>(_ =>
{
    var client = new MongoClient(mongoUri);
    return client.GetDatabase(mongoDbName);
});
services.AddSingleton<IDatabaseService, DatabaseService>();
services.AddSingleton<IOpenAiService>(_ => new ClaudeService(claudeApiKey));
services.AddSingleton<ConversationStateManager>();
services.AddSingleton<RegistrationHandler>(_ => new RegistrationHandler(
    _.GetRequiredService<ITelegramBotClient>(),
    _.GetRequiredService<IDatabaseService>(),
    _.GetRequiredService<ConversationStateManager>(),
    allowedTeachers));
services.AddSingleton<TeacherHandler>();
services.AddSingleton<StudentHandler>(sp => new StudentHandler(
    sp.GetRequiredService<ITelegramBotClient>(),
    sp.GetRequiredService<IDatabaseService>(),
    sp.GetRequiredService<ConversationStateManager>(),
    sp.GetRequiredService<IOpenAiService>()));
services.AddSingleton<WordEntryHandler>();
services.AddSingleton<QuizHandler>();
services.AddSingleton<BotService>(sp => new BotService(
    sp.GetRequiredService<ITelegramBotClient>(),
    sp.GetRequiredService<IDatabaseService>(),
    sp.GetRequiredService<ConversationStateManager>(),
    sp.GetRequiredService<RegistrationHandler>(),
    sp.GetRequiredService<TeacherHandler>(),
    sp.GetRequiredService<StudentHandler>(),
    sp.GetRequiredService<WordEntryHandler>(),
    sp.GetRequiredService<QuizHandler>(),
    allowedTeachers));

using var provider = services.BuildServiceProvider();

// Create collections and indexes on startup
await provider.GetRequiredService<IDatabaseService>().InitializeAsync();

var bot = provider.GetRequiredService<BotService>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await bot.StartAsync(cts.Token);
try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (TaskCanceledException) { }

Console.WriteLine("Bot stopped.");
