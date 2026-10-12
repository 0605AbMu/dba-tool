using System.Text.Json;
using System.Text.RegularExpressions;

namespace DbaTool.Api.Common;

public class ProcessErrorDiagnostic
{
    public string Stage { get; set; } = "UNKNOWN";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? TechnicalDetails { get; set; }
    public List<string> ActionableSteps { get; set; } = [];
    public string? SuggestedActionUrl { get; set; }
}

public class ExecutionStateChangedEvent
{
    public int ExecutionId { get; set; }
    public int? JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = "INIT"; // INIT | CONNECT | EXTRACT | COMPRESS | ENCRYPT | STORAGE | RETENTION | NOTIFY | COMPLETED | FAILED
    public string StageTitle { get; set; } = string.Empty;
    public string StageDescription { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public long BytesProcessed { get; set; }
    public string Throughput { get; set; } = string.Empty;
    public string Elapsed { get; set; } = string.Empty;
    public string? CurrentDatabase { get; set; }
    public string? CurrentTable { get; set; }
    public bool IsError { get; set; }
    public ProcessErrorDiagnostic? Diagnostic { get; set; }
}

public static class ProcessDiagnosticEngine
{
    public static readonly Dictionary<string, (string Title, string Description)> StageDefinitions = new()
    {
        ["INIT"] = ("Tizim va sozlamalarni tekshirish", "Konfiguratsiya va dbb binary muhiti tayyorlanmoqda"),
        ["CONNECT"] = ("Bazaga ulanish va autentifikatsiya", "Ma'lumotlar bazasi serveri bilan aloqa o'rnatilmoqda"),
        ["EXTRACT"] = ("Jadval va ma'lumotlarni eksport qilish", "Baza jadvallari va strukturasi yuklab olinmoqda"),
        ["COMPRESS"] = ("Zstandard oqimli siqish", "Hajmni ixchamlashtirish uchun oqimli siqish bajarilmoqda"),
        ["ENCRYPT"] = ("Shifrlash va nazorat summasi", "Age/GPG shifrlash va SHA/MD5 yaxlitligi hisoblanmoqda"),
        ["STORAGE"] = ("Xotiraga saqlash va Sidecar v2", "Zaxira fayli va .json metama'lumotlari yozilmoqda"),
        ["MAINTAIN"] = ("Baza profilaktikasi (Maintenance)", "DBCC CHECKDB, CHECK TABLE, Optimize, Vacuum yoki Index tekshiruvi bajarilmoqda"),
        ["RETENTION"] = ("Eski nusxalarni tozalash (GFS)", "Belgilangan muddatdan oshgan eski nusxalar o'chirilmoqda"),
        ["NOTIFY"] = ("Xabardor qilish (Alerts)", "Telegram, Slack yoki Email orqali xabar yuborilmoqda"),
        ["COMPLETED"] = ("Muvaffaqiyatli yakunlandi", "Barcha bosqichlar to'liq va muvaffaqiyatli yakunlandi"),
        ["FAILED"] = ("Jarayon xatolik bilan to'xtadi", "Bosqichlardan birida kutilmagan xatolik yuz berdi")
    };

    public static string DetectStageFromLog(string line, JsonElement? json, string currentStage)
    {
        if (string.IsNullOrWhiteSpace(line)) return currentStage;

        var lower = line.ToLowerInvariant();

        if (json.HasValue)
        {
            var root = json.Value;
            var action = root.TryGetProperty("action", out var aProp) ? aProp.GetString()?.ToLowerInvariant() : "";
            var status = root.TryGetProperty("status", out var sProp) ? sProp.GetString()?.ToLowerInvariant() : "";

            if (action == "startup") return "INIT";
            if (status == "progress")
            {
                if (root.TryGetProperty("table", out var t) && !string.IsNullOrWhiteSpace(t.GetString()))
                    return "EXTRACT";
                return "COMPRESS";
            }
            if (action == "backup" && status == "complete") return "STORAGE";
            if (action == "prune") return "RETENTION";
            if (action == "restore") return "EXTRACT";
            if (action == "verify") return "ENCRYPT";
            if (action == "maintain") return "MAINTAIN";
        }

        // Text heuristics
        if (lower.Contains("reading config") || lower.Contains("db-backup 5.") || lower.Contains("resolving"))
            return "INIT";

        if (lower.Contains("maintain") || lower.Contains("dbcc") || lower.Contains("checkdb") || lower.Contains("check table") || lower.Contains("vacuum") || lower.Contains("optimize") || lower.Contains("reindex") || lower.Contains("compact") || lower.Contains("memory-purge"))
            return "MAINTAIN";

        if (lower.Contains("connecting") || lower.Contains("dial tcp") || lower.Contains("handshake") || lower.Contains("authenticat"))
            return "CONNECT";

        if (lower.Contains("splitting into per-database") || lower.Contains("dumping") || lower.Contains("table") || lower.Contains("schema"))
            return "EXTRACT";

        if (lower.Contains("compress") || lower.Contains("zstd") || lower.Contains("gzip") || lower.Contains("compression"))
            return "COMPRESS";

        if (lower.Contains("encrypt") || lower.Contains("checksum") || lower.Contains("sha256") || lower.Contains("md5"))
            return "ENCRYPT";

        if (lower.Contains("writing to") || lower.Contains("uploading") || lower.Contains("sidecar") || lower.Contains("saved to"))
            return "STORAGE";

        if (lower.Contains("pruning") || lower.Contains("retention") || lower.Contains("removing expired"))
            return "RETENTION";

        if (lower.Contains("notification") || lower.Contains("telegram") || lower.Contains("slack") || lower.Contains("webhook"))
            return "NOTIFY";

        return currentStage;
    }

    public static ProcessErrorDiagnostic DiagnoseFailure(string fullLog, string currentStage, int exitCode)
    {
        var log = fullLog ?? string.Empty;
        var lower = log.ToLowerInvariant();

        // 0. MSSQL DBCC CHECKDB Permission Error (Maintenance)
        if (lower.Contains("does not have permission to run dbcc checkdb") || (lower.Contains("dbcc") && (lower.Contains("permission") || lower.Contains("denied"))))
        {
            var matchUser = Regex.Match(log, @"User '([^']+)' does not have permission", RegexOptions.IgnoreCase);
            var matchDb = Regex.Match(log, @"database '([^']+)'", RegexOptions.IgnoreCase);
            var user = matchUser.Success ? matchUser.Groups[1].Value : "foydalanuvchi";
            var dbName = matchDb.Success ? matchDb.Groups[1].Value : "baza";

            return new ProcessErrorDiagnostic
            {
                Stage = "MAINTAIN",
                Title = "MSSQL DBCC CHECKDB Ruxsat Yetishmadi",
                Summary = $"'{user}' foydalanuvchisida '{dbName}' bazasida DBCC CHECKDB profilaktika amalini bajarish huquqi yo'q.",
                TechnicalDetails = ExtractErrorLine(log, "permission") ?? ExtractErrorLine(log, "DBCC"),
                ActionableSteps =
                [
                    $"MSSQL Server Management Studio (SSMS) orqali '{user}' foydalanuvchisiga '{dbName}' bazasida 'db_owner' rolini bering: ALTER ROLE db_owner ADD MEMBER [{user}];",
                    $"Yoki server miqyosida 'sysadmin' rolini bering: ALTER SERVER ROLE sysadmin ADD MEMBER [{user}];",
                    "Agar ushbu foydalanuvchi faqat zaxiralash (Read-only) uchun mo'ljallangan bo'lsa, Backup Job sozlamalarida Profilaktika (Check Tables / DBCC) funksiyasini o'chirib qo'ying."
                ],
                SuggestedActionUrl = "/jobs"
            };
        }

        // 0.1 Maintenance General Failure (✗ marker or error during maintain)
        if (log.Contains('✗') || (currentStage == "MAINTAIN" && (lower.Contains("error") || lower.Contains("failed"))))
        {
            var errorLine = ExtractErrorLine(log, "✗") ?? ExtractErrorLine(log, "error") ?? ExtractErrorLine(log, "failed");
            return new ProcessErrorDiagnostic
            {
                Stage = "MAINTAIN",
                Title = "Baza Profilaktikasida Xatolik (Maintenance Failed)",
                Summary = errorLine ?? "Baza profilaktikasi (maintenance) amallaridan birini bajarishda xatolik yuz berdi.",
                TechnicalDetails = errorLine,
                ActionableSteps =
                [
                    "Ma'lumotlar bazasi foydalanuvchisida texnik xizmat ko'rsatish (CHECK, VACUUM, OPTIMIZE) huquqlari mavjudligini tekshiring.",
                    "Ulanish sozlamalari va server holatini tekshiring."
                ],
                SuggestedActionUrl = "/jobs"
            };
        }

        // 1. MSSQL Authentication / Login failure
        if (lower.Contains("login failed for user"))
        {
            var match = Regex.Match(log, @"login failed for user '([^']+)'", RegexOptions.IgnoreCase);
            var user = match.Success ? match.Groups[1].Value : "ko'rsatilgan foydalanuvchi";
            return new ProcessErrorDiagnostic
            {
                Stage = "CONNECT",
                Title = "MSSQL Autentifikatsiya Xatosi (Login Failed)",
                Summary = $"SQL Server '{user}' foydalanuvchisi uchun login yoki parolni qabul qilmadi.",
                TechnicalDetails = ExtractErrorLine(log, "login failed"),
                ActionableSteps =
                [
                    "Ulanish profilida (Connection Profile) foydalanuvchi nomi va parol to'g'ri kiritilganini tekshiring.",
                    "Agar parol maxsus belgilarni o'z ichiga olsa, file:// yoki env:// formatidan foydalaning.",
                    "MSSQL serverda 'SQL Server and Windows Authentication mode' yoqilganligini tekshiring."
                ],
                SuggestedActionUrl = "/profiles?tab=connections"
            };
        }

        // 2. PostgreSQL Authentication failure
        if (lower.Contains("password authentication failed for user"))
        {
            var match = Regex.Match(log, @"password authentication failed for user ""([^""]+)""", RegexOptions.IgnoreCase);
            var user = match.Success ? match.Groups[1].Value : "foydalanuvchi";
            return new ProcessErrorDiagnostic
            {
                Stage = "CONNECT",
                Title = "PostgreSQL Autentifikatsiya Xatosi",
                Summary = $"PostgreSQL server '{user}' foydalanuvchisi parolini rad etdi.",
                TechnicalDetails = ExtractErrorLine(log, "authentication failed"),
                ActionableSteps =
                [
                    "PostgreSQL foydalanuvchi parolini qayta tekshiring.",
                    "pg_hba.conf faylida ushbu IP manzil va foydalanuvchi uchun md5/scram-sha-256 ruxsati borligini tekshiring."
                ],
                SuggestedActionUrl = "/profiles?tab=connections"
            };
        }

        // 3. MySQL / MariaDB Access Denied
        if (lower.Contains("access denied for user"))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "CONNECT",
                Title = "MySQL / MariaDB Ruxsat Xatosi (Access Denied)",
                Summary = "MySQL server ko'rsatilgan foydalanuvchi va parol bilan ulanishni rad etdi.",
                TechnicalDetails = ExtractErrorLine(log, "access denied"),
                ActionableSteps =
                [
                    "MySQL foydalanuvchi nomi va parolini tekshiring.",
                    "Foydalanuvchi ushbu xostdan (% yoki localhost) ulanish huquqiga ega ekanligini tasdiqlang."
                ],
                SuggestedActionUrl = "/profiles?tab=connections"
            };
        }

        // 4. Connection Refused / Port unreachable
        if (lower.Contains("connection refused") || lower.Contains("actively refused"))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "CONNECT",
                Title = "Serverga Ulanib Bo'lmadi (Connection Refused)",
                Summary = "Ma'lumotlar bazasi serveri ishlamayapti yoki ko'rsatilgan portda ulanishlarni qabul qilmayapti.",
                TechnicalDetails = ExtractErrorLine(log, "refused"),
                ActionableSteps =
                [
                    "Ma'lumotlar bazasi xizmati (service) serverda ishlab turganini tekshiring.",
                    "Xost manzili (IP) va port raqami (masalan, MSSQL: 1433, PG: 5432, MySQL: 3306) to'g'ri kiritilganini tekshiring.",
                    "Xavfsizlik devori (Firewall) ushbu portni bloklamaganligiga ishonch hosil qiling."
                ],
                SuggestedActionUrl = "/profiles?tab=connections"
            };
        }

        // 5. Network Timeout
        if (lower.Contains("i/o timeout") || lower.Contains("timed out") || lower.Contains("deadline exceeded"))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "CONNECT",
                Title = "Tarmoq Vaqti Tugadi (Network Timeout)",
                Summary = "Ma'lumotlar bazasi serveri belgilangan vaqt ichida javob bermadi.",
                TechnicalDetails = ExtractErrorLine(log, "timeout"),
                ActionableSteps =
                [
                    "Server IP manzili va ichki tarmoq (VPN/VLAN) aloqasini tekshiring.",
                    "Ulanish profilida TimeoutSeconds parametrini kattaroq qiymatga (masalan, 60s) oshiring."
                ],
                SuggestedActionUrl = "/profiles?tab=connections"
            };
        }

        // 6. Database Not Found
        if (lower.Contains("database does not exist") || lower.Contains("unknown database") || lower.Contains("cannot open database"))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "EXTRACT",
                Title = "Ma'lumotlar Bazasi Topilmadi",
                Summary = "Zaxira qilinishi so'ralgan ma'lumotlar bazasi serverda mavjud emas yoki nomi noto'g'ri.",
                TechnicalDetails = ExtractErrorLine(log, "database"),
                ActionableSteps =
                [
                    "Backup Job sozlamalarida Database Names qatorini tekshiring.",
                    "Browse DBs tugmasi orqali mavjud bazalar ro'yxatini qayta tanlang."
                ],
                SuggestedActionUrl = "/jobs"
            };
        }

        // 7. Disk Full / No Space Left
        if (lower.Contains("no space left on device"))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "STORAGE",
                Title = "Disk Xotirasi To'ldi (No Space Left)",
                Summary = "Zaxira nusxasini yozish uchun diskda yetarli bo'sh joy qolmadi.",
                TechnicalDetails = ExtractErrorLine(log, "no space left"),
                ActionableSteps =
                [
                    "Server diskini tozalang yoki saqlash profilini (Storage Profile) boshqa kengroq diskka yoki S3 Cloud-ga yo'naltiring.",
                    "Zaxiralarni qisqartirish (Prune) mexanizmini ishga tushiring."
                ],
                SuggestedActionUrl = "/profiles?tab=storage"
            };
        }

        // 8. Storage Permission Denied
        if (lower.Contains("permission denied") && (currentStage == "STORAGE" || lower.Contains("storage") || lower.Contains("mkdir")))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "STORAGE",
                Title = "Xotiraga Yozish Huquqi Yo'q (Permission Denied)",
                Summary = "Zaxira katalogiga fayl yozish huquqi cheklangan.",
                TechnicalDetails = ExtractErrorLine(log, "permission denied"),
                ActionableSteps =
                [
                    "Storage profilidagi katalog yo'lini (path) tekshiring.",
                    "Katalogga 'chmod 755' yoki 'chmod 700' ruxsati berilganini tasdiqlang."
                ],
                SuggestedActionUrl = "/profiles?tab=storage"
            };
        }

        // 9. Config file missing
        if (lower.Contains("open ./data/configs/db-backup.yml: no such file"))
        {
            return new ProcessErrorDiagnostic
            {
                Stage = "INIT",
                Title = "Konfiguratsiya Fayli Sinxronlanmagan",
                Summary = "db-backup.yml konfiguratsiyasi hali generatsiya qilinmagan.",
                TechnicalDetails = ExtractErrorLine(log, "no such file"),
                ActionableSteps =
                [
                    "Dashboard yoki Config Studio sahifasiga o'tib 'Regenerate YAML' tugmasini bosing."
                ],
                SuggestedActionUrl = "/config"
            };
        }

        // Generic fallback diagnostic
        var fallbackSummary = ExtractLastMeaningfulError(log) ?? $"Jarayon xatolik bilan yakunlandi (Exit code: {exitCode}).";
        return new ProcessErrorDiagnostic
        {
            Stage = currentStage == "COMPLETED" ? "FAILED" : currentStage,
            Title = $"Jarayon Xatoligi ({currentStage} bosqichi)",
            Summary = fallbackSummary,
            TechnicalDetails = fallbackSummary,
            ActionableSteps =
            [
                "Xatolik tafsilotlarini ko'rib chiqing va sozlamalarni tekshiring.",
                "Zarur bo'lsa Backup Job-ni 'Dry Run' (simulyatsiya) rejimida sinab ko'ring."
            ],
            SuggestedActionUrl = "/jobs"
        };
    }

    private static string? ExtractErrorLine(string log, string keyword)
    {
        var lines = log.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines.Reverse())
        {
            if (l.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return l.Trim();
            }
        }
        return null;
    }

    private static string? ExtractLastMeaningfulError(string log)
    {
        var lines = log.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines.Reverse())
        {
            var trimmed = l.Trim();
            if (trimmed.Contains('✗') ||
                trimmed.Contains("does not have permission", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("fatal", StringComparison.OrdinalIgnoreCase))
            {
                // If it's json, try to extract error property
                if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        if (doc.RootElement.TryGetProperty("error", out var ep)) return ep.GetString();
                        if (doc.RootElement.TryGetProperty("msg", out var mp)) return mp.GetString();
                    }
                    catch { }
                }
                return trimmed;
            }
        }
        return null;
    }
}
