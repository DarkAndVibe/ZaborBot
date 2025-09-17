using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;

namespace ZaborCalculator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "🤖 ZaborCalculator Bot - Запущен";

            // Создаем хост для фоновой службы
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<TelegramBotClient>(provider =>
                    {
                        var botToken = "7614942762:AAGboaA9MoTVUHhl4aXdTj6Wf1PgYZPRW2Q";
                        return new TelegramBotClient(botToken);
                    });
                    services.AddHostedService<BotBackgroundService>();
                    services.AddSingleton<BotService>();
                })
                .Build();

            await host.RunAsync();
        }
    }

    // Фоновая служба для постоянной работы бота
    public class BotBackgroundService : BackgroundService
    {
        private readonly TelegramBotClient _botClient;
        private readonly BotService _botService;

        public BotBackgroundService(TelegramBotClient botClient, BotService botService)
        {
            _botClient = botClient;
            _botService = botService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Устанавливаем информацию о боте
                await SetBotInfoAsync();

                var me = await _botClient.GetMeAsync(cancellationToken: stoppingToken);
                Console.WriteLine($"✅ Бот @{me.Username} успешно запущен!");
                Console.WriteLine($"📛 Имя: {me.FirstName}");
                Console.WriteLine("🔄 Бот работает в фоновом режиме...");
                Console.WriteLine("⏹️ Для остановки нажмите Ctrl+C");

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                var updateReceiver = new QueuedUpdateReceiver(_botClient, receiverOptions);

                await foreach (var update in updateReceiver.WithCancellation(stoppingToken))
                {
                    await _botService.HandleUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏹️ Бот остановлен.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критическая ошибка: {ex.Message}");
            }
        }

        private async Task SetBotInfoAsync()
        {
            try
            {
                // Здесь можно установить информацию о боте через BotFather
                Console.WriteLine("ℹ️  Информация о боте:");
                Console.WriteLine("📛 Имя: ZaborCalculator Bot");
                Console.WriteLine("👤 Username: ZaborCalculatorBot");
                Console.WriteLine("📝 Описание: Калькулятор стоимости забора");
                Console.WriteLine("🎨 Аватар: Установите через @BotFather");
                Console.WriteLine("💬 Команды: /start, /help, /restart");

                // Для установки аватарки нужно через @BotFather:
                // 1. Напишите /setuserpic в @BotFather
                // 2. Выберите вашего бота
                // 3. Отправьте изображение
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Не удалось установить информацию о боте: {ex.Message}");
            }
        }
    }

    // Сервис для обработки логики бота
    public class BotService
    {
        public static Dictionary<long, UserState> userStates = new Dictionary<long, UserState>();
        private readonly TelegramBotClient _botClient;

        public BotService(TelegramBotClient botClient)
        {
            _botClient = botClient;
        }

        public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message)
                return;

            var chatId = message.Chat.Id;
            var userState = GetUserState(chatId);

            try
            {
                Console.WriteLine($"📨 Сообщение от {message.Chat.FirstName} ({chatId}): {message.Text}");

                if (message.Text != null && message.Text.StartsWith("/"))
                {
                    await HandleCommand(message, userState, cancellationToken);
                    return;
                }

                await HandleUserInput(message, userState, cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "⚠️ Произошла ошибка. Попробуйте еще раз.",
                    cancellationToken: cancellationToken);
                Console.WriteLine($"❌ Ошибка от пользователя {chatId}: {ex.Message}");
            }
        }

        private UserState GetUserState(long chatId)
        {
            if (!userStates.ContainsKey(chatId))
                userStates[chatId] = new UserState();
            return userStates[chatId];
        }

        private async Task HandleCommand(Message message, UserState state, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var command = message.Text?.ToLower().Trim();

            switch (command)
            {
                case "/start":
                case "/restart":
                    state.Reset();
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "👋 Привет! Я — ZaborCalculator Bot 🤖\n\nЯ помогу рассчитать стоимость забора.\n\nВыбери тип материала:\n" +
                              "1. 🏗️ Профнастил\n2. 🕸️ Сетка рабица\n3. 🌳 Дерево\n\nНапиши номер варианта.",
                        cancellationToken: cancellationToken);
                    state.Step = Step.AwaitingMaterial;
                    break;

                case "/help":
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "🛠️ *Команды бота:*\n" +
                              "/start - начать расчёт\n" +
                              "/restart - сбросить и начать заново\n" +
                              "/help - эта справка\n\n" +
                              "📊 *Как работает:*\n" +
                              "1. Выбираете материал\n" +
                              "2. Указываете длину\n" +
                              "3. Указываете высоту\n" +
                              "4. Получаете расчет!",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    break;

                default:
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "❌ Неизвестная команда. Напиши /start для начала.",
                        cancellationToken: cancellationToken);
                    break;
            }
        }

        private async Task HandleUserInput(Message message, UserState state, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text?.Trim();

            if (string.IsNullOrEmpty(text))
                return;

            switch (state.Step)
            {
                case Step.None:
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "👋 Напиши /start, чтобы начать расчёт стоимости забора.",
                        cancellationToken: cancellationToken);
                    break;

                case Step.AwaitingMaterial:
                    if (int.TryParse(text, out int materialChoice) && materi
                        ъ\
                            alChoice >= 1 && materialChoice <= 3)
                    {
                        state.Material = materialChoice switch
                        {
                            1 => "🏗️ Профнастил",
                            2 => "🕸️ Сетка рабица",
                            3 => "🌳 Дерево",
                            _ => "❓ Неизвестно"
                        };
                        state.MaterialPricePerMeter = materialChoice switch
                        {
                            1 => 1500,
                            2 => 800,
                            3 => 2000,
                            _ => 0
                        };
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "📏 Введите длину забора в метрах (например: 25):",
                            cancellationToken: cancellationToken);
                        state.Step = Step.AwaitingLength;
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "❌ Пожалуйста, введите число от 1 до 3.",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case Step.AwaitingLength:
                    if (double.TryParse(text, out double length) && length > 0)
                    {
                        state.Length = length;
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "📐 Введите высоту забора в метрах (например: 2):",
                            cancellationToken: cancellationToken);
                        state.Step = Step.AwaitingHeight;
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "❌ Пожалуйста, введите положительное число.",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case Step.AwaitingHeight:
                    if (double.TryParse(text, out double height) && height > 0)
                    {
                        state.Height = height;
                        double totalCost = state.Length * state.MaterialPricePerMeter * state.Height;

                        var result = new StringBuilder();
                        result.AppendLine("✅ *Расчёт завершён!*");
                        result.AppendLine($"▫️ *Материал:* {state.Material}");
                        result.AppendLine($"▫️ *Длина:* {state.Length} м");
                        result.AppendLine($"▫️ *Высота:* {state.Height} м");
                        result.AppendLine($"▫️ *Цена за пог.м:* {state.MaterialPricePerMeter} руб");
                        result.AppendLine($"💰 *Итого:* {totalCost:N0} руб");
                        result.AppendLine("");
                        result.AppendLine("🔄 Для нового расчета напишите /start");

                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: result.ToString(),
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                        state.Reset();
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "❌ Пожалуйста, введите положительное число.",
                            cancellationToken: cancellationToken);
                    }
                    break;
            }
        }
    }

    public class UserState
    {
        public Step Step { get; set; } = Step.None;
        public string Material { get; set; }
        public double MaterialPricePerMeter { get; set; }
        public double Length { get; set; }
        public double Height { get; set; }

        public void Reset()
        {
            Step = Step.None;
            Material = null;
            MaterialPricePerMeter = 0;
            Length = 0;
            Height = 0;
        }
    }

    public enum Step
    {
        None,
        AwaitingMaterial,
        AwaitingLength,
        AwaitingHeight
    }
}