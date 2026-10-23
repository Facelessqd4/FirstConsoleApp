using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System;
using System.IO;
using System.Security.Permissions;

class Program
{
    const string DateFormat = "yyyy-MM-dd HH:mm";
    static List<string> rejected = new List<string>();
    static Dictionary<string, DateTime> brokenNow = new Dictionary<string, DateTime>();
    static Dictionary<string, TimeSpan> downtime = new Dictionary<string, TimeSpan>();
    static Dictionary<string, int> failureCount = new Dictionary<string, int>();
    static readonly DateTime ShiftStart = new DateTime(2027, 1, 20, 8, 0, 0);
    static readonly DateTime ShiftEnd = new DateTime(2027, 1, 20, 20, 0, 0);
    static List<string> stillBroken = new List<string>();
    static DateTime lastTime = DateTime.MinValue;
    static List<string> warnings = new List<string>();
    static void Main()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "events.log");
        string[] lines = ReadLog(path);

        if (lines.Length == 0)
        {
            Console.WriteLine("Нечего обрабатывать.");
            return;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            ProcessLine(lines[i], i + 1);
        }
        CloseShift();

        string report = BuildReport();
        Console.WriteLine(report);
        SaveReports(report);
    }
    static string[] ReadLog(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Не удалось прочитать журнал: {ex.Message}");
            return Array.Empty<string>();
        }
    }
    static void ProcessLine(string line, int lineNumber)
    {
        if (line.Trim() == "")
        {
            return;
        }

        string[] parts = line.Split(';');

        if (parts.Length != 3)
        {
            rejected.Add($"Строка {lineNumber}: должно быть 3 части, а их {parts.Length}");
            return;
        }

        string timeText = parts[0].Trim();
        string code = parts[1].Trim();
        string eventType = parts[2].Trim();

        bool isDate = DateTime.TryParseExact(
        timeText, DateFormat, CultureInfo.InvariantCulture,
        DateTimeStyles.None, out DateTime time); 
        
        if (!isDate)
        {
            rejected.Add($"Строка {lineNumber}: неверная дата «{timeText}»");
            return;
        }
        
        if(time < ShiftStart || time > ShiftEnd)
        {
            warnings.Add($"Строка {lineNumber}: Указанное время вне рабочего дня");
            return;
        }
        
        if (!IsValidCode(code))
        {
            rejected.Add($"Строка {lineNumber}: неверный код станка «{code}»");
            return;
        }

        if (eventType != "ОТКАЗ" && eventType != "ПОЧИНЕН")
        {
            rejected.Add($"Строка {lineNumber}: неизвестное событие «{eventType}»");
            return;
        }
        if (time < lastTime)
        {
            warnings.Add($"Строка {lineNumber}: время пошло назад ({time:HH:mm} после {lastTime:HH:mm})");
        }
        else
        {
            lastTime = time;
        }
        if (eventType == "ОТКАЗ")
        {
            RegisterFailure(code, time, lineNumber);
        }
        else
        {
            RegisterRepair(code, time, lineNumber);
        }
       
    }
    static bool IsValidCode(string code)
    {
        if (code.Length != 6 || !code.StartsWith("EQ-", StringComparison.Ordinal))
        {
            return false;
        }

        bool isNumber = int.TryParse(code.Substring(3), out int number);
        return isNumber && number >= 1 && number <= 15;
    }
    static void RegisterFailure(string code, DateTime time, int lineNumber)
    {
        if (brokenNow.ContainsKey(code))
        {
            warnings.Add($"Строка {lineNumber}: {code} снова сломался, хотя ещё не починен");
            return;
        }

        brokenNow[code] = time;

        if (failureCount.ContainsKey(code))
        {
            failureCount[code]++;
        }
        else
        {
            failureCount[code] = 1;
        }
    }
    static void RegisterRepair(string code, DateTime time, int lineNumber)
    {
        if (!brokenNow.TryGetValue(code, out DateTime brokenAt))
        {
            warnings.Add($"Строка {lineNumber}: {code} починен, хотя не ломался");
            return;
        }

        if (time < brokenAt)
        {
            warnings.Add($"Строка {lineNumber}: {code} починен раньше, чем сломался");
            return;
        }

        AddDowntime(code, time - brokenAt);
        brokenNow.Remove(code);
    }
    static void AddDowntime(string code, TimeSpan duration)
    {
        if (downtime.ContainsKey(code))
        {
            downtime[code] += duration;
        }
        else
        {
            downtime[code] = duration;
        }
    }
    static string FormatDuration(TimeSpan time)
    {
        return $"{(int)time.TotalHours}:{time.Minutes:00}";
    }
    static List<string> GetSortedCodes()
    {
        List<string> codes = new List<string>(downtime.Keys);
        codes.Sort();
        return codes;
    }
    static void CloseShift()
    {
        foreach (KeyValuePair<string, DateTime> pair in brokenNow)
        {
            AddDowntime(pair.Key, ShiftEnd - pair.Value);
            stillBroken.Add(pair.Key);
        }

        brokenNow.Clear();
    }
    static string BuildReport()
    {
        StringBuilder report = new StringBuilder();

        report.AppendLine($"ОТЧЁТ ЗА СМЕНУ {ShiftStart:dd.MM.yyyy} ({ShiftStart:HH:mm}–{ShiftEnd:HH:mm})");
        report.AppendLine();
        report.AppendLine($"{"Станок",-11}{"Отказов",-10}Простой");

        TimeSpan total = TimeSpan.Zero;
        string worstCode = "";
        TimeSpan worstTime = TimeSpan.Zero;

        foreach (string code in GetSortedCodes())
        {
            TimeSpan time = downtime[code];
            int failures = failureCount[code];

            string mark = "";

            if (stillBroken.Contains(code))
            {
                mark = "  ДО СИХ ПОР СЛОМАН";
            }

            report.AppendLine($"{code,-11}{failures,-10}{FormatDuration(time)}{mark}");

            total += time;

            if (time > worstTime)
            {
                worstTime = time;
                worstCode = code;
            }
        }

        report.AppendLine();
        bool printed = false;
        foreach (string code in GetSortedCodes())
        {
            int failures = failureCount[code];
    
            if (failures >= 2)
            {
                if (!printed)
                {
                    report.AppendLine("Нужна проверка (2 отказа и больше):");
                    printed = true;
                }
                report.AppendLine($" {code} - отказов: {failures}");
            }
        }

        if (printed)
        {
            report.AppendLine();
        }
            report.AppendLine($"Общий простой: {FormatDuration(total)}");

        if (worstCode != "")
        {
            report.AppendLine($"Хуже всех: {worstCode} ({FormatDuration(worstTime)})");
        }
            AppendList(report, "Отброшено строк", rejected);
            AppendList(report, "Предупреждения", warnings);

        return report.ToString();
    }
    static void AppendList(StringBuilder report, string title, List<string> items)
    {
        report.AppendLine();
        report.AppendLine($"{title}: {items.Count}");

        foreach (string item in items)
        {
            report.AppendLine($"  {item}");
        }
    }
    static string BuildCsv()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("станок;отказов;простой_часов");

        foreach (string code in GetSortedCodes())
        {
            string hours = downtime[code].TotalHours.ToString("0.00", CultureInfo.InvariantCulture);
            csv.AppendLine($"{code};{failureCount[code]};{hours}");
        }

        return csv.ToString();
    }
    static void SaveReports(string report)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "reports");
        string date = ShiftStart.ToString("yyyy-MM-dd");

        string reportPath = Path.Combine(folder, $"shift-report-{date}.txt");
        string csvPath = Path.Combine(folder, $"shift-summary-{date}.csv");

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(reportPath, report);
            File.WriteAllText(csvPath, BuildCsv());

            Console.WriteLine($"Сохранено: {reportPath}");
            Console.WriteLine($"Сохранено: {csvPath}");
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("Нет прав на запись в папку reports.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Не удалось сохранить отчёт: {ex.Message}");
        }
    }
}