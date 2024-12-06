using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static ITelegramBotClient botClient;
    private static string userFullName;
    private static string supervisorName;
    private static bool isDataConfirmed = false; // Состояние подтверждения данных
    private static string dbPath = "work_reasons.db"; // Путь к базе данных

    static async Task Main()
    {
        // Инициализация базы данных
        InitializeDatabase();

        botClient = new TelegramBotClient("YOUR_BOT_TOKEN_HERE");

        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Hello, I am user {me.Id} and my name is {me.FirstName}.");

        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            cancellationToken: cancellationToken
        );

        Console.WriteLine("Press any key to exit");
        Console.ReadKey();
        cts.Cancel();
    }

    private static void InitializeDatabase()
    {
        // Проверяем, существует ли файл базы данных
        if (!File.Exists(dbPath))
        {
            SQLiteConnection.CreateFile(dbPath);
        }

        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            connection.Open();
            string createTableQuery = "CREATE TABLE IF NOT EXISTS Reasons (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserFullName TEXT, SupervisorName TEXT, Reason TEXT, Date TEXT)";
            using (var command = new SQLiteCommand(createTableQuery, connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message && update.Message?.Text != null)
        {
            var chatId = update.Message.Chat.Id;

            if (isDataConfirmed)
            {
                switch (update.Message.Text.ToLower())
                {
                    case "выход на маршрут":
                        await botClient.SendTextMessageAsync(chatId, "Вы вышли на маршрут. Удачной работы !");
                        break;

                    case "сход с маршрута":
                        await botClient.SendTextMessageAsync(chatId, "Вы сошли с маршрута. Пожалуйста, опишите причину:");
                        break;

                    default:
                        await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите действие: 'Выход на маршрут' или 'Сход с маршрута'.");
                        break;
                }
                return; // Выходим из метода, если пользователь уже подтвердил свои данные
            }

            switch (update.Message.Text.ToLower())
            {
                case "/start":
                    await ShowRoleOptions(chatId);
                    break;

                case "мерчендайзер":
                    await AskForFullName(chatId);
                    break;

                case "супервайзер":
                    await botClient.SendTextMessageAsync(chatId, "Вы выбрали супервайзера. Пожалуйста, выберите мерчендайзера.");
                    break;

                case "выход на маршрут":
                    await ConfirmWorkStart(chatId);
                    break;

                case "сход с маршрута":
                    await botClient.SendTextMessageAsync(chatId, "Вы сошли с маршрута. Пожалуйста, опишите причину:");
                    break;

                default:
                    if (userFullName == null)
                    {
                        userFullName = update.Message.Text; // Сохраняем ФИО
                        await AskForSupervisor(chatId); // Запрашиваем имя супервайзера
                    }
                    else if (supervisorName == null)
                    {
                        if (update.Message.Text.ToLower() == "татьяна" || update.Message.Text.ToLower() == "иван")
                        {
                            supervisorName = update.Message.Text; // Сохраняем имя супервайзера
                            await ConfirmDetails(chatId); // Подтверждаем данные
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите корректного супервайзера: татьяна или иван.");
                        }
                    }
                    else if (update.Message.Text.StartsWith("причина:"))
                    {
                        string reason = update.Message.Text.Substring(8); // Извлекаем причину
                        await HandleReasonForNotWorking(chatId, reason); // Обрабатываем причину
                    }
                    else
                    {
                        await HandleConfirmation(chatId, update.Message.Text); // Обработка подтверждения
                    }
                    break;
            }
        }
    }

    private static async Task HandleReasonForNotWorking(long chatId, string reason)
    {
        // Сохранение причины в базе данных
        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            connection.Open();
            string insertQuery = "INSERT INTO Reasons (User FullName, SupervisorName, Reason, Date) VALUES (@User FullName, @SupervisorName, @Reason, @Date)";
            using (var command = new SQLiteCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@User FullName", userFullName);
                command.Parameters.AddWithValue("@SupervisorName", supervisorName);
                command.Parameters.AddWithValue("@Reason", reason);
                command.Parameters.AddWithValue("@Date", DateTime.Now.ToString("dd.MM.yyyy"));
                command.ExecuteNonQuery();
            }
        }

        await botClient.SendTextMessageAsync(chatId, "Причина успешно сохранена. Вы собираетесь отрабатывать? Пожалуйста, введите 'да' или 'нет'.", replyMarkup: new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton("да"),
            new KeyboardButton("нет")
        })
        {
            ResizeKeyboard = true
        });
    }

    private static async Task ShowRoleOptions(long chatId)
    {
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton("мерчендайзер"),
            new KeyboardButton("супервайзер")
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите вашу роль:", replyMarkup: replyKeyboard);
    }

    private static async Task AskForFullName(long chatId)
    {
        await botClient.SendTextMessageAsync(chatId, "Пожалуйста, введите ваше ФИО:");
    }

    private static async Task AskForSupervisor(long chatId)
    {
        await botClient.SendTextMessageAsync(chatId, "Пожалуйста, введите имя супервайзера (татьяна или иван):");
    }

    private static async Task ConfirmDetails(long chatId)
    {
        isDataConfirmed = true;
        await botClient.SendTextMessageAsync(chatId, "Ваши данные подтверждены. Вы можете начать работу.");
    }

    private static async Task HandleConfirmation(long chatId, string message)
    {
        // Логика обработки подтверждения
        await botClient.SendTextMessageAsync(chatId, $"Вы подтвердили: {message}");
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Error occurred: {exception.Message}");
        return Task.CompletedTask;
    }
    private static async Task ConfirmWorkStart(long chatId)
    {
        // Проверяем, что пользователь ввел свои данные
        if (string.IsNullOrEmpty(userFullName) || string.IsNullOrEmpty(supervisorName))
        {
            await botClient.SendTextMessageAsync(chatId, "Пожалуйста, введите свои данные перед подтверждением начала работы.");
            return;
        }

        // Сохраняем данные пользователя в базе данных
        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            connection.Open();

            string insertQuery = "INSERT INTO Reasons (UserFullName, SupervisorName, Date) VALUES (@UserFullName, @SupervisorName, @Date)";
            using (var command = new SQLiteCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@UserFullName", userFullName);
                command.Parameters.AddWithValue("@SupervisorName", supervisorName);
                command.Parameters.AddWithValue("@Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                await command.ExecuteNonQueryAsync();
            }
        }

        // Подтверждаем начало работы и сбрасываем состояние данных
        await botClient.SendTextMessageAsync(chatId, "Вы успешно вышли на маршрут. Удачной работы!");
        userFullName = null;
        supervisorName = null;
        isDataConfirmed = false;
    }
}
