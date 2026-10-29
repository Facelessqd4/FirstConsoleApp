using System;
using System.ComponentModel.Design;
using System.Security.Cryptography;
using System.Text;

class Program
{
    const string ExpectedHash = "6C6D0FCDBD26A8ECAC9F4A32E89BD8C45DB72CFA66E5FAE6C44E15BAD6954973";
    static bool Check(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
        string content = File.ReadAllText(path).Trim();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        string hex = Convert.ToHexString(hash);
        return string.Equals(hex, ExpectedHash, StringComparison.OrdinalIgnoreCase);
        }

        catch(IOException)
        {
            return false;
        }
    }

    static void Main()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "menu.list");
        string Keypath = Path.Combine(AppContext.BaseDirectory, "pass.key");
        string balance = Path.Combine(AppContext.BaseDirectory, "balance.bal");

        bool isAdmin = Check(Keypath);
        if (isAdmin) 
        {
            Console.WriteLine("\nНайден пароль для доступа к админ-панели.");
        }

        
        string[] lines = ReadFile(path);

        string[] menuItems = GetValidMenu(lines, isAdmin);

        List<string> UserOrder = new List<string>();

        while(true)
        {
            Console.WriteLine("\nВыберите действие: \n 1. Меню\n 2. Создать заказ\n 3. Удалить заказ\n 4. Корзина\n 5. Оплатить");
            Console.WriteLine(" 0. Выход");

            if (isAdmin)
            {
                Console.WriteLine(" 6. [Админ-панель] Изменить баланс");
            }

            Console.Write("\nВведите номер действия: ");

            string input = Console.ReadLine() ?? "";
            
            if(input == "")
            {
                Console.WriteLine("Действие не выбрано!");
            }

            if(input == "0")
            {
                Console.WriteLine("Удачи!");
                break;
            }

            switch (input)
            {
                case "1":
                {
                    ShowMenu(menuItems);
                    break;
                }
                case "2":
                {
                    CreateOrder(menuItems, UserOrder);
                    break;
                }
                case "3":
                {
                    DeleteOrders(UserOrder);
                    break;
                }
                case "4":
                {
                    Cart(UserOrder);
                    break;
                }
                case "5":
                {
                    Pay(UserOrder, balance);
                    break;
                }
                case "6":
                {
                    if (isAdmin)
                    {
                        ChangeBalance(balance);
                    }
                    else
                    {
                        Console.WriteLine("Неизвестная команда. Попробуйте еще раз.");
                    }

                    break;
                }
                default:
                {
                    Console.WriteLine("Неизвестная команда. Попробуйте еще раз.");
                     break;   
                }

            }


        }
    }
    static int ReadBalance(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "0");
            return 0;
        }

        try
        {
            string text = File.ReadAllText(path).Trim();
            if (int.TryParse(text, out int balance) && balance >= 0)
            {
                return balance;
            }
            Console.WriteLine("Файл баланса повреждён, баланс считается равным 0.");
            return 0;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Не удалось прочитать баланс: {ex.Message}");
            return 0;
        }
    }
    static void WriteBalance(string path, int balance)
    {
        try
        {
            File.WriteAllText(path, balance.ToString());
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Не удалось сохранить баланс: {ex.Message}");
        }
    }
    static string[] ReadFile(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
         catch (IOException ex)
        {
            Console.WriteLine($"Не удалось прочитать файл меню: {ex.Message}");
            return Array.Empty<string>();
        }
    }
    static string ReadLine(string line, int lineNumber)
    {
        string[] parts = line.Split(';');

        if (parts.Length != 2)
        {
            return $"Строка {lineNumber}: должно быть 2 части, а их {parts.Length}";
        }

        return string.Empty;
    }
    static void ShowMenu(string[] menuItems)
    {
        Console.WriteLine("\n------- МЕНЮ -------");
        for (int i = 0; i < menuItems.Length; i++)
        {
            string[] parts = menuItems[i].Split(';');
            Console.WriteLine($"{i + 1}. {parts[0].Trim()} — {parts[1].Trim()} руб.");
        }
    }
    static string[] GetValidMenu(string[] lines, bool isAdmin)
    {
        List<string> validLines = new List<string>();
        bool hasSeparatorPrinted = false;

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string currentLine = lines[i];
            string error = ReadLine(currentLine, lineNumber);

            if (error != string.Empty)
            {
                if (isAdmin)
                {
                    if (!hasSeparatorPrinted)
                    {
                        Console.WriteLine("\n-----------------------------------------------------------------------------\n");
                        hasSeparatorPrinted = true;
                    }
                    Console.WriteLine($"Ошибка в файле меню -> {error}");
                }
                continue; 
            }

            string[] parts = currentLine.Split(';');
            string name = parts[0].Trim();
            string price = parts[1].Trim();
            int parsedPrice;

            if (!int.TryParse(price, out parsedPrice) || parsedPrice < 0)
            {
                if (isAdmin)
                {
                    if (!hasSeparatorPrinted)
                    {
                        Console.WriteLine("\n-----------------------------------------------------------------------------\n");
                        hasSeparatorPrinted = true;
                    }
                    Console.WriteLine($"Ошибка в файле меню -> Строка {lineNumber}: цена не указана.");
                }
                continue;
            }

            if (name == "")
            {
                if (isAdmin)
                {
                    if (!hasSeparatorPrinted)
                    {
                        Console.WriteLine("\n-----------------------------------------------------------------------------\n");
                        hasSeparatorPrinted = true;
                    }
                    Console.WriteLine($"Ошибка в файле меню -> Строка {lineNumber}: имя не указано.");
                }
                continue;
            }

            validLines.Add(currentLine);
        }

        if (hasSeparatorPrinted)
        {
            Console.WriteLine("\n-----------------------------------------------------------------------------");
        }

        return validLines.ToArray();
    }
    static void PrintCart(List<string> UserOrder)
    {
        int total = 0;
        for (int i = 0; i < UserOrder.Count; i++)
        {
            string[] parts = UserOrder[i].Split(';');
            string name = parts[0].Trim();
            int price = int.Parse(parts[1].Trim());

            total += price;
            Console.WriteLine($" {i + 1}. {name} — {price} руб.");
        }
        Console.WriteLine("----------------------------------------");
        Console.WriteLine($" Итого: {total} руб.\n");
    }
    static void CreateOrder(string[] menuItems, List<string> UserOrder)
    {
        if (menuItems.Length == 0)
        {
            Console.WriteLine("Ошибка: Меню пустое или не загружено!");
            return;
        }

        int itemNumber = 0;
        Console.WriteLine();
        
        while (true)
        {
            if (UserOrder.Count > 0)
            {
                PrintCart(UserOrder);
            }

            Console.Write("Введите номер блюда для добавления в заказ (или 0 для завершения): ");
            string choiceInput = Console.ReadLine() ?? "";

            if (choiceInput == "0")
            {
                Console.WriteLine("\nДобавление заказа завершено. Возвращаюсь в главное меню...");
                return;
            }

            if (!int.TryParse(choiceInput, out itemNumber) || itemNumber < 1 || itemNumber > menuItems.Length)
            {
                Console.WriteLine($"Ошибка! Пожалуйста, введите число от 1 до {menuItems.Length}.");
                continue;
            }

            int UsersIndex = itemNumber - 1;
            string selectedLine = menuItems[UsersIndex];
            string[] parts = selectedLine.Split(';');
            string dish = parts[0].Trim();
            int price = int.Parse(parts[1].Trim());
            UserOrder.Add(selectedLine);

            Console.WriteLine($"\nБлюдо \"{dish}\" за {price} добавлено в вашу корзину!\n");
        }
    }
    static void DeleteOrders(List<string> UserOrder)
    {
        while (true)
        {
            if (UserOrder.Count < 1)
            {
                Console.WriteLine("\nКорзина пуста.");
                return;
            }

            Console.WriteLine("\n------- КОРЗИНА -------\n");

            PrintCart(UserOrder);

            Console.Write("\nВведите номер позиции для удаления (0 для возврата): ");

            string input = Console.ReadLine() ?? "";

            if (input == "0")
            {
                return;
            }

            if (!int.TryParse(input, out int itemNumber) || itemNumber < 1 || itemNumber > UserOrder.Count)
            {
                Console.WriteLine($"Ошибка! Пожалуйста, введите число от 1 до {UserOrder.Count}.");
                continue;
            }

            int index = itemNumber - 1;
            string name = UserOrder[index].Split(';')[0].Trim();
            UserOrder.RemoveAt(index);

            Console.WriteLine($"\nБлюдо \"{name}\" удалено из корзины!");
        }
    }
    static void Cart(List<string> UserOrder)
    {
        if(UserOrder.Count < 1)
        {
            Console.WriteLine("Ошибка: Вы не добавили в корзину ни 1 блюда");
            return;
        }

        Console.WriteLine("\n------- КОРЗИНА -------");
        
        PrintCart(UserOrder);
    }
    static void Pay(List<string> UserOrder, string balancePath)
    {
        if (UserOrder.Count < 1)
        {
            Console.WriteLine("Ошибка: корзина пуста, платить не за что.");
            return;
        }

        int total = 0;
        foreach (string line in UserOrder)
        {
            total += int.Parse(line.Split(';')[1].Trim());
        }

        int balance = ReadBalance(balancePath);
        Console.WriteLine($"\nСумма заказа: {total} руб. Ваш баланс: {balance} руб.");

        if (balance < total)
        {
            Console.WriteLine($"Недостаточно средств, не хватает {total - balance} руб.");
            return;
        }

        WriteBalance(balancePath, balance - total);
        UserOrder.Clear();
        Console.WriteLine($"Оплата прошла успешно! Остаток: {balance - total} руб.");
    }
    static void ChangeBalance(string balancePath)
    {
        Console.WriteLine($"Текущий баланс: {ReadBalance(balancePath)} руб.");
        Console.Write("Введите новый баланс: ");
        string input = Console.ReadLine() ?? "";

        if (!int.TryParse(input, out int newBalance) || newBalance < 0)
        {
            Console.WriteLine("Ошибка! Нужно целое неотрицательное число.");
            return;
        }

        WriteBalance(balancePath, newBalance);
        Console.WriteLine("Баланс обновлён.");
    }
}