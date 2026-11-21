using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCMonitorPro;

public sealed class Finding
{
    public string Sev { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Advice { get; }
    public HashSet<string> Tags { get; set; } = new();
    public Finding(string sev, string title, string detail = "", string advice = "")
    { Sev = sev; Title = title; Detail = detail; Advice = advice; }
}

public sealed record Hypothesis(string Key, string Name, int Score, List<string> Evidence, string[] Advice);

/// <summary>Marco de comparação ("antes/depois de uma mudança").</summary>
public sealed record Milestone(DateTime Time, string Note);

public sealed class DiagnosticReport
{
    public string Created { get; set; } = "";
    public string Scenario { get; set; } = "geral";
    public List<string> System { get; } = new();
    public List<Finding> Findings { get; set; } = new();
    public List<Finding> Relevant { get; set; } = new();
    public List<Finding> Others { get; set; } = new();
    public List<Hypothesis> Hypotheses { get; set; } = new();
    public string Confidence { get; set; } = "baixa";
    public List<string> CollectionErrors { get; set; } = new();

    public Hypothesis? Top => Hypotheses.Count > 0 ? Hypotheses[0] : null;
    public string ScenarioLabel => (Diagnostics.Scenarios.TryGetValue(Scenario, out var s) ? s : Diagnostics.Scenarios["geral"]).Name;

    /// <summary>Relatório em texto; por padrão termina com os comandos sugeridos ligados às causas prováveis.</summary>
    public string ToText(bool includeCommands = true)
    {
        var L = new List<string> { $"RELATÓRIO DE DIAGNÓSTICO — {Created}", $"Foco: {ScenarioLabel}", "" };
        L.Add("SISTEMA"); L.AddRange(System.Select(s => "  " + s)); L.Add("");
        var t = Top;
        if (t != null)
        {
            L.Add($"CAUSA MAIS PROVÁVEL (confiança {Confidence}): {t.Name}"); L.Add("");
            L.Add("  Evidências:"); L.AddRange(t.Evidence.Select(e => "   - " + e));
            L.Add("  O que fazer:"); L.AddRange(t.Advice.Select((a, i) => $"   {i + 1}. {a}")); L.Add("");
            var others = Hypotheses.Skip(1).Where(h => h.Score > 0).ToList();
            if (others.Count > 0)
            {
                L.Add("OUTRAS HIPÓTESES"); L.AddRange(others.Select(h => $"  - {h.Name} (pontos: {h.Score})")); L.Add("");
            }
        }
        else { L.Add("Nenhuma causa provável identificada para este foco."); L.Add(""); }

        void Dump(IEnumerable<Finding> items)
        {
            foreach (var f in items)
            {
                L.Add($"[{Diagnostics.SevLabel[f.Sev]}] {f.Title}");
                if (f.Detail != "") L.AddRange(f.Detail.Split('\n').Select(ln => "    " + ln.TrimEnd('\r')));
                if (f.Advice != "") L.Add("    → " + f.Advice);
                L.Add("");
            }
        }
        L.Add("ACHADOS DO FOCO"); Dump(Relevant);
        if (Others.Count > 0) { L.Add("OUTROS ACHADOS (fora do foco)"); Dump(Others); }
        if (CollectionErrors.Count > 0) { L.Add("AVISOS DA COLETA"); L.AddRange(CollectionErrors.Select(e => "  - " + e)); }
        if (includeCommands) { L.Add(""); L.Add(CommandSuggestions.ForReport(this)); }
        return string.Join("\n", L);
    }
}

public sealed record Scenario(string Name, HashSet<string>? Focus);

/// <summary>Tabelas de regras do diagnóstico (códigos de tela azul, cenários, hipóteses e conselhos).</summary>
public static class Diagnostics
{
    public static readonly Dictionary<long, (string Name, string Cat)> BugChecks = new()
    {
        [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", "mem"), [0x1A] = ("MEMORY_MANAGEMENT", "mem"),
        [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", "mem"), [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", "mem"),
        [0x4E] = ("PFN_LIST_CORRUPT", "mem"), [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", "mem"),
        [0x18] = ("REFERENCE_BY_POINTER", "mem"), [0x19] = ("BAD_POOL_HEADER", "mem"), [0xC2] = ("BAD_POOL_CALLER", "mem"),
        [0xDE] = ("POOL_CORRUPTION_IN_FILE_AREA", "mem"), [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", "mem"),
        [0x13A] = ("KERNEL_MODE_HEAP_CORRUPTION", "mem"), [0x189] = ("BAD_OBJECT_HEADER", "mem"),
        [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "driver"), [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "driver"),
        [0x133] = ("DPC_WATCHDOG_VIOLATION", "driver"), [0x9F] = ("DRIVER_POWER_STATE_FAILURE", "driver"),
        [0x116] = ("VIDEO_TDR_FAILURE", "gpu"), [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "gpu"),
        [0x119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", "gpu"), [0x141] = ("VIDEO_ENGINE_TIMEOUT_DETECTED", "gpu"),
        [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", "disk"), [0x77] = ("KERNEL_STACK_INPAGE_ERROR", "disk"),
        [0x24] = ("NTFS_FILE_SYSTEM", "disk"), [0x154] = ("UNEXPECTED_STORE_EXCEPTION", "disk"),
        [0x124] = ("WHEA_UNCORRECTABLE_ERROR", "hw"), [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", "hw"),
        [0x7F] = ("UNEXPECTED_KERNEL_MODE_TRAP", "hw"), [0xEF] = ("CRITICAL_PROCESS_DIED", "other"),
        [0xF4] = ("CRITICAL_OBJECT_TERMINATION", "other"),
    };

    public static readonly Dictionary<string, string> CatNames = new()
    {
        ["mem"] = "memória/corrupção", ["driver"] = "driver", ["gpu"] = "placa de vídeo", ["disk"] = "disco",
        ["hw"] = "hardware (CPU/placa)", ["other"] = "outro",
    };

    private static HashSet<string> Set(params string[] t) => new(t);

    /// <summary>Tipos de problema que o usuário pode escolher e as etiquetas dos achados que importam (null = tudo).</summary>
    public static readonly Dictionary<string, Scenario> Scenarios = new()
    {
        ["geral"] = new("Check-up geral (tudo)", null),
        ["lento"] = new("PC lento ou travando", Set("perf", "mem", "disk", "startup", "thermal", "security")),
        ["quedas"] = new("Tela azul, reinícios ou desligamentos", Set("crash", "power", "hw", "os", "thermal", "mem")),
        ["apps"] = new("Programas fechando ou não respondendo", Set("apps", "mem", "gpu", "os")),
        ["calor"] = new("Superaquecimento ou ventoinhas barulhentas", Set("thermal", "perf", "power")),
        ["disco"] = new("Disco cheio, lento ou com erros", Set("disk", "startup")),
        ["rede"] = new("Internet lenta ou sem conexão", Set("net")),
        ["video"] = new("Problemas de vídeo, tela ou jogos", Set("gpu", "video")),
        ["update"] = new("Windows Update ou sistema corrompido", Set("update", "os", "disk", "security")),
    };

    /// <summary>Ordem fixa das hipóteses (desempata pontuações iguais, como no original).</summary>
    public static readonly string[] HypOrder =
    {
        "ram", "gpu", "disk", "driver", "thermal", "power", "hw", "os", "lowram", "bgload", "startup", "diskslow",
        "netlocal", "netisp", "netdns", "netadapter", "security", "update",
    };

    public static readonly Dictionary<string, string> HypNames = new()
    {
        ["ram"] = "Memória RAM instável (EXPO/overclock ou pente defeituoso)",
        ["gpu"] = "Driver ou hardware da placa de vídeo",
        ["disk"] = "Disco ou controladora de armazenamento com defeito",
        ["driver"] = "Driver ou software específico",
        ["thermal"] = "Superaquecimento",
        ["power"] = "Fonte de alimentação / energia elétrica",
        ["hw"] = "CPU, placa-mãe ou barramento (erros WHEA)",
        ["os"] = "Versão do Windows (build de testes ou atualização problemática)",
        ["lowram"] = "Pouca memória livre ou muitos programas abertos",
        ["bgload"] = "Algum processo consumindo muita CPU em segundo plano",
        ["startup"] = "Muitos programas iniciando junto com o Windows",
        ["diskslow"] = "Disco lento, ocupado ou quase cheio",
        ["netlocal"] = "Problema na rede local (Wi-Fi, cabo ou roteador)",
        ["netisp"] = "Problema no provedor de internet",
        ["netdns"] = "Problema de DNS",
        ["netadapter"] = "Adaptador de rede desconectado ou limitado",
        ["security"] = "Proteção desativada ou possível ameaça",
        ["update"] = "Windows Update com falhas ou serviço desativado",
    };

    public static readonly Dictionary<string, HashSet<string>> HypTags = new()
    {
        ["ram"] = Set("crash", "mem", "apps", "perf"), ["gpu"] = Set("gpu", "video", "crash", "apps"),
        ["disk"] = Set("disk", "crash", "perf"), ["driver"] = Set("crash", "video", "apps"),
        ["thermal"] = Set("thermal", "crash", "perf", "power"), ["power"] = Set("power", "crash"),
        ["hw"] = Set("crash", "hw"), ["os"] = Set("os", "crash", "update", "apps"), ["lowram"] = Set("perf", "mem"),
        ["bgload"] = Set("perf", "thermal"), ["startup"] = Set("perf", "startup"), ["diskslow"] = Set("disk", "perf"),
        ["netlocal"] = Set("net"), ["netisp"] = Set("net"), ["netdns"] = Set("net"), ["netadapter"] = Set("net"),
        ["security"] = Set("security", "perf"), ["update"] = Set("update", "os"),
    };

    public static readonly Dictionary<string, string[]> HypAdvice = new()
    {
        ["ram"] = new[]
        {
            "Na BIOS, desative o XMP/EXPO/DOCP e deixe a memória no padrão. Use o PC normalmente por 2 a 3 dias.",
            "Rode o MemTest86 (pendrive), 4 passadas completas. Se der erro, teste um pente por vez, no mesmo slot.",
            "Atualize a BIOS da placa-mãe (melhora a compatibilidade de memória).",
            "Se ainda houver erros com a RAM no padrão, o pente está com defeito: acione a garantia.",
        },
        ["gpu"] = new[]
        {
            "Reinstale o driver da placa de vídeo com o DDU (modo seguro) e use uma versão estável.",
            "Se houver GPU integrada e dedicada, confirme em qual delas o monitor está ligado.",
            "Confira cabos de energia da placa e o encaixe no slot PCIe; monitore a temperatura.",
        },
        ["disk"] = new[]
        {
            "Faça backup agora dos dados importantes.",
            "Veja o SMART no CrystalDiskInfo e rode 'chkdsk /scan' (aba Ferramentas).",
            "Troque o cabo SATA/porta e teste o disco isolado.",
        },
        ["driver"] = new[]
        {
            "Descubra o driver culpado abrindo os minidumps (C:\\Windows\\Minidump) no BlueScreenView ou WinDbg.",
            "Atualize chipset, rede, áudio e Bluetooth; remova softwares de overlay/otimização que injetam drivers.",
        },
        ["thermal"] = new[]
        {
            "Limpe poeira, revise a pasta térmica e a curva das ventoinhas; garanta boa circulação de ar no gabinete.",
            "Deixe o widget com as temperaturas visível e veja o valor no momento do problema.",
        },
        ["power"] = new[]
        {
            "Teste outra tomada/estabilizador/nobreak e confira os cabos da fonte.",
            "Quedas sem tela azul (reinício seco) sugerem energia ou travamento total de hardware.",
        },
        ["hw"] = new[]
        {
            "Remova overclock/PBO/EXPO, atualize a BIOS e verifique temperatura e energia da CPU.",
            "Se persistir com tudo no padrão, pode ser defeito de CPU ou placa-mãe (garantia).",
        },
        ["os"] = new[]
        {
            "O Windows em canal Insider pode ter bugs de driver/kernel. Considere voltar para uma versão estável " +
            "(pode exigir reinstalar, dependendo do canal e da build). Faça backup antes.",
        },
        ["lowram"] = new[]
        {
            "Feche programas e abas que você não usa (veja os maiores consumidores no relatório).",
            "Se a RAM vive acima de 85%, considere adicionar mais memória.",
            "Reinicie o PC para liberar vazamentos de programas abertos há muito tempo.",
        },
        ["bgload"] = new[]
        {
            "Abra o Gerenciador de Tarefas (Ctrl+Shift+Esc), ordene por CPU e veja o que mais consome.",
            "Confira se não é verificação do antivírus, indexação ou atualização em andamento (terminam sozinhas).",
            "Se o processo for desconhecido, pesquise o nome antes de encerrar e rode uma verificação do Defender.",
        },
        ["startup"] = new[]
        {
            "Gerenciador de Tarefas > Inicializar: desative os programas que você não precisa ao ligar o PC.",
            "Programas desativados continuam funcionando quando você os abre manualmente.",
        },
        ["diskslow"] = new[]
        {
            "Libere espaço: mantenha ao menos 15% do disco livre.",
            "Se o Windows está em HDD, migrar para SSD é a maior melhora de velocidade possível.",
            "Veja em Gerenciador de Tarefas > Desempenho > Disco qual programa está usando o disco.",
        },
        ["netlocal"] = new[]
        {
            "Reinicie o roteador (tire da tomada por 30 s) e teste de novo.",
            "No Wi-Fi: aproxime-se do roteador, use a banda de 5 GHz ou teste por cabo.",
            "Troque o cabo de rede e a porta do roteador.",
        },
        ["netisp"] = new[]
        {
            "O roteador responde mas a internet não: reinicie modem e roteador e confira as luzes do modem.",
            "Se continuar, é do provedor: ligue para o suporte informando horário e os testes feitos.",
        },
        ["netdns"] = new[]
        {
            "Troque o DNS do adaptador para 1.1.1.1 e 8.8.8.8 (Configurações > Rede > propriedades do adaptador).",
            "Em um terminal, rode 'ipconfig /flushdns'.",
        },
        ["netadapter"] = new[]
        {
            "Verifique se o cabo está conectado, o Wi-Fi ligado e o modo avião desativado.",
            "Atualize ou reinstale o driver do adaptador no Gerenciador de Dispositivos.",
        },
        ["security"] = new[]
        {
            "Ative a proteção em tempo real em Segurança do Windows e atualize as definições.",
            "Rode uma verificação completa; se houver ameaça, siga a remoção sugerida.",
        },
        ["update"] = new[]
        {
            "Reinicie o PC e rode o Windows Update; se falhar, use a solução de problemas do Windows Update.",
            "Rode DISM RestoreHealth e SFC (aba Ferramentas) e libere espaço no C: (15 GB ou mais).",
        },
    };

    /// <summary>WHEA-Logger: erros que o hardware já corrigiu (PCIe/machine check/cache corrigidos); não são falhas.</summary>
    public static readonly HashSet<int> WheaCorrectedIds = new() { 17, 19, 46, 47 };

    public static readonly string[] SystemApps =
    {
        "dwm.exe", "msmpeng.exe", "explorer.exe", "svchost.exe", "tiworker.exe", "taskmgr.exe",
        "startmenuexperiencehost.exe", "runtimebroker.exe", "searchhost.exe", "sihost.exe",
    };

    public static readonly Dictionary<string, string> CrashCodes = new()
    {
        ["c0000005"] = "acesso inválido à memória", ["c0000374"] = "corrupção de heap", ["c0000409"] = "falha de segurança/pilha",
    };

    public static readonly Dictionary<string, int> SevOrder = new() { ["crit"] = 0, ["warn"] = 1, ["info"] = 2, ["ok"] = 3 };
    public static readonly Dictionary<string, string> SevLabel = new() { ["crit"] = "CRÍTICO", ["warn"] = "ATENÇÃO", ["info"] = "INFO", ["ok"] = "OK" };
}

/// <summary>Leitura tolerante do JSON bruto da coleta (campos ausentes/nulos viram valores neutros).</summary>
internal static class J
{
    public static JsonNode? Get(JsonNode? n, string key) => (n as JsonObject)?[key];

    public static JsonObject Obj(JsonNode? n, string key) => Get(n, key) as JsonObject ?? new JsonObject();

    public static List<JsonNode?> AsList(JsonNode? x)
    {
        if (x == null) return new();
        if (x is JsonArray a) return a.ToList();
        if (x is JsonValue v && v.TryGetValue<string>(out var s) && s == "") return new();
        return new() { x };
    }

    public static List<JsonNode?> List(JsonNode? n, string key) => AsList(Get(n, key));

    /// <summary>Texto como o Python mostraria (null/ausente = "").</summary>
    public static string S(JsonNode? n, string key) => Str(Get(n, key));

    public static string Str(JsonNode? x)
    {
        if (x == null) return "";
        if (x is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<bool>(out var b)) return b ? "True" : "False";
            if (v.TryGetValue<double>(out var d)) return d.ToString("R", CultureInfo.InvariantCulture);
        }
        return x.ToJsonString();
    }

    public static double? Num(JsonNode? x)
    {
        if (x is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    public static double? D(JsonNode? n, string key) => Num(Get(n, key));
    public static double D0(JsonNode? n, string key) => D(n, key) ?? 0;
    public static bool? B(JsonNode? n, string key) => Get(n, key) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static DateTime? Ts(JsonNode? x)
    {
        var s = Str(x);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static DateTime? Ts(JsonNode? n, string key) => Ts(Get(n, key));

    public static string F0(double d) => d.ToString("0", CultureInfo.InvariantCulture);
    public static string F1(double d) => d.ToString("0.0", CultureInfo.InvariantCulture);
    public static string Dm(DateTime? d) => d?.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) ?? "?";

    /// <summary>Equivalente a Counter.most_common: mais frequentes primeiro, empates na ordem em que apareceram.</summary>
    public static List<KeyValuePair<K, int>> MostCommon<K>(IEnumerable<K> items) where K : notnull
    {
        var order = new List<K>(); var cnt = new Dictionary<K, int>();
        foreach (var i in items) { if (!cnt.ContainsKey(i)) { cnt[i] = 0; order.Add(i); } cnt[i]++; }
        return order.Select(k => new KeyValuePair<K, int>(k, cnt[k])).OrderByDescending(kv => kv.Value).ToList();
    }
}

/// <summary>Análise do diagnóstico: roda as seções e monta hipóteses e recomendações em português.</summary>
public static class DiagnosticsAnalyzer
{
    private sealed class Ctx
    {
        public JsonNode Raw; public IReadOnlyList<LastValues>? HistoryInfo; public Milestone? Milestone; public RecentStats? Recent;
        public DateTime Now; public string Scenario;
        public DiagnosticReport Rep = new();
        public Dictionary<string, int> Score = new();
        public Dictionary<string, List<string>> Evid = new();
        public HashSet<string> Tags = new();
        public JsonObject Osd, Board; public List<JsonNode?> Ram; public string Build;
        // calculados por uma seção e lidos por outras
        public List<DateTime> Power = new(); public List<(DateTime? T, long? Code)> BugEvents = new(); public int N30;
        public List<long> Codes = new(); public bool LowC; public JsonObject Pw = new();

        public Ctx(JsonNode raw, IReadOnlyList<LastValues>? hist, Milestone? ms, DateTime now, string scenario, RecentStats? recent)
        {
            Raw = raw; HistoryInfo = hist; Milestone = ms; Recent = recent; Now = now; Scenario = scenario;
            Rep.Created = now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture); Rep.Scenario = scenario;
            Rep.CollectionErrors = J.List(raw, "errors").Select(J.Str).ToList();
            Osd = J.Obj(raw, "os"); Board = J.Obj(raw, "board"); Ram = J.List(raw, "ram"); Build = J.S(Osd, "build");
        }

        public void T(params string[] tags) => Tags = new HashSet<string>(tags);
        public void F(string sev, string title, string detail = "", string advice = "")
            => Rep.Findings.Add(new Finding(sev, title, detail, advice) { Tags = new HashSet<string>(Tags) });
        public void Add(string key, int pts, string text)
        {
            Score[key] = Score.GetValueOrDefault(key) + pts;
            if (!Evid.TryGetValue(key, out var l)) Evid[key] = l = new();
            l.Add(text);
        }
    }

    public static DiagnosticReport Analyze(JsonNode? raw, IReadOnlyList<LastValues>? historyInfo = null, Milestone? milestone = null,
        DateTime? now = null, string scenario = "geral", RecentStats? recent = null)
    {
        if (!Diagnostics.Scenarios.ContainsKey(scenario)) scenario = "geral";
        var ctx = new Ctx(raw ?? new JsonObject(), historyInfo, milestone, now ?? DateTime.Now, scenario, recent);
        foreach (var section in new Action<Ctx>[]
        {
            SecSystem, SecOutages, SecBsod, SecMemDiag, SecRam, SecWhea, SecDisks, SecApps, SecGpu, SecPerf, SecThermal,
            SecSecurity, SecOs, SecUpdate, SecNet, SecHistory, SecMilestone,
        })
            section(ctx);
        return Finish(ctx);
    }

    private static string Join(string sep, IEnumerable<string> items) => string.Join(sep, items);

    // ------------------------------------------------------------ seções
    private static void SecSystem(Ctx c)
    {
        var rep = c.Rep;
        if (c.Board.Count > 0)
        {
            rep.System.Add($"{J.S(c.Board, "maker")} {J.S(c.Board, "model")} (placa {J.S(c.Board, "board")})");
            rep.System.Add($"CPU: {J.S(c.Board, "cpu")} — {Or(J.S(c.Board, "cores"), "?")} núcleos / {Or(J.S(c.Board, "threads"), "?")} threads");
            var bd = J.Ts(c.Board, "biosDate");
            rep.System.Add($"BIOS: {Or(J.S(c.Board, "biosVersion"), "?")} ({(bd?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "data desconhecida")})");
        }
        if (c.Osd.Count > 0)
        {
            var head = string.Join(" ", new[] { Or(J.S(c.Osd, "caption"), "Windows"), J.S(c.Osd, "display") }.Where(x => x != ""));
            var ubr = J.D(c.Osd, "ubr");
            rep.System.Add(head + $" — build {c.Build}" + (ubr is > 0 ? "." + J.F0(ubr.Value) : ""));
        }
        if (c.Ram.Count > 0)
        {
            double tot = c.Ram.Sum(m => J.D0(m, "gb"));
            rep.System.Add($"RAM: {J.F0(tot)} GB em {c.Ram.Count} pente(s): " +
                           Join("; ", c.Ram.Select(m => $"{J.S(m, "maker")} {J.S(m, "part")} {J.S(m, "gb")}GB")));
        }
        foreach (var g in J.List(c.Raw, "gpus")) rep.System.Add($"GPU: {J.S(g, "name")} (driver {J.S(g, "driver")})");
    }

    private static string Or(string s, string def) => s == "" ? def : s;

    private static void SecOutages(Ctx c)
    {
        c.T("crash", "power");
        var power = J.List(c.Raw, "ev_power").Select(e => J.Ts(e, "t")).Where(t => t != null).Select(t => t!.Value).OrderBy(t => t).ToList();
        var bugEvents = J.List(c.Raw, "ev_bugcheck").Select(e => (T: J.Ts(e, "t"), Code: HexCode(J.S(e, "m")))).ToList();
        var bugTimes = bugEvents.Where(b => b.T != null).Select(b => b.T!.Value).ToList();
        int n30 = power.Count;
        int n7 = power.Count(t => t >= c.Now.AddDays(-7));
        var unmatched = power.Where(t => !bugTimes.Any(b => { var s = (b - t).TotalSeconds; return s >= -10 && s <= 180; })).ToList();
        if (n30 == 0) c.F("ok", "Nenhuma queda inesperada nos últimos 30 dias");
        else
        {
            string sev = (n7 >= 3 || n30 >= 8) ? "crit" : "warn";
            var det = new List<string> { $"{n30} em 30 dias, {n7} nos últimos 7. A mais recente: {J.Dm(power[^1])}." };
            if (n30 >= 2)
            {
                double spanH = (power[^1] - power[0]).TotalSeconds / 3600;
                det.Add($"Entre a primeira e a última, uma queda a cada ~{J.F1(spanH / (n30 - 1))} h em média.");
            }
            det.Add($"{n30 - unmatched.Count} terminaram em tela azul com código registrado; " +
                    $"{unmatched.Count} sem tela azul registrada (travamento total, botão/reset ou perda de energia).");
            c.F(sev, $"{n30} quedas/reinícios inesperados em 30 dias ({n7} na última semana)", Join("\n", det),
                "Anote a data de cada mudança que fizer (BIOS, driver, Windows) com 'Marcar mudança' para comparar.");
            if (n30 >= 3 && (double)unmatched.Count / n30 >= 0.4)
                c.Add("power", 12, $"{unmatched.Count} de {n30} quedas sem tela azul registrada (parece reinício seco)");
        }
        c.Power = power; c.BugEvents = bugEvents; c.N30 = n30;
    }

    private static long? HexCode(string msg)
    {
        var m = Regex.Match(msg ?? "", "0x([0-9a-fA-F]{1,16})");
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static (string Name, string Cat) BugInfo(long code) =>
        Diagnostics.BugChecks.TryGetValue(code, out var i) ? i : ($"código 0x{code:X}", "other");

    private static void SecBsod(Ctx c)
    {
        c.T("crash", "mem");
        var codesList = c.BugEvents.Where(b => b.Code != null).Select(b => b.Code!.Value).ToList();
        var codes = J.MostCommon(codesList);
        if (codes.Count > 0)
        {
            int memDistinct = codes.Count(k => BugInfo(k.Key).Cat == "mem");
            int total = codes.Sum(k => k.Value);
            var lines = codes.Take(14).Select(k => $"0x{k.Key:X}  {BugInfo(k.Key).Name}  ×{k.Value}  ({Diagnostics.CatNames[BugInfo(k.Key).Cat]})");
            string sev, title, adv = "";
            if (memDistinct >= 4)
            {
                sev = "crit";
                title = $"{codes.Count} códigos diferentes de tela azul em {total} ocorrências, {memDistinct} deles ligados a memória";
                adv = "Muitos códigos diferentes sem causa única é o padrão clássico de RAM/overclock instável (ou driver que corrompe memória).";
                c.Add("ram", 12, $"{memDistinct} códigos de tela azul diferentes ligados a memória");
            }
            else { sev = "warn"; title = $"{total} telas azuis registradas ({codes.Count} códigos)"; }
            c.F(sev, title, Join("\n", lines), adv);
            var (topC, topN) = (codes[0].Key, codes[0].Value);
            var cat = BugInfo(topC).Cat;
            if (total >= 3 && (double)topN / total >= 0.5 && cat is "gpu" or "driver" or "disk" or "hw")
            {
                c.Add(cat, 15, $"{topN} de {total} telas azuis são {BugInfo(topC).Name} (0x{topC:X})");
                c.F("warn", $"Um código domina: {BugInfo(topC).Name} (0x{topC:X}) em {topN} de {total}", $"Categoria: {Diagnostics.CatNames[cat]}.");
            }
        }
        else if (c.N30 > 0)
            c.F("info", "Há quedas, mas nenhum código de tela azul foi lido do registro",
                "Pode ser reinício seco (energia/hardware) ou o registro foi limpo.");
        c.Codes = codesList;
    }

    private static readonly Regex BadMem = new("defeit|defective|bad memory|danific|défectu|defectu", RegexOptions.IgnoreCase);

    private static void SecMemDiag(Ctx c)
    {
        c.T("crash", "mem", "apps", "perf");
        foreach (var e in J.List(c.Raw, "ev_memdiag"))
        {
            string msg = J.S(e, "m"); var eid = J.D(e, "id"); var when = J.Ts(e, "t");
            if (BadMem.IsMatch(msg) || eid == 1201)
            {
                c.F("crit", $"O Windows detectou memória defeituosa ({J.Dm(when)})", Cut(msg, 300),
                    "Isso prova erro físico/instabilidade na RAM. Faça o teste com EXPO desativado e o MemTest86.");
                c.Add("ram", 15, $"Teste de memória do Windows encontrou regiões defeituosas ({J.Dm(when)})");
            }
            else if (eid == 1101)
                c.F("ok", $"Teste de memória do Windows sem erros ({J.Dm(when)})",
                    "O teste rápido do Windows pode não achar erros que o MemTest86 acha.");
        }
    }

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];

    private static void SecRam(Ctx c)
    {
        c.T("crash", "mem", "apps");
        if (c.Ram.Count == 0) return;
        var speeds = c.Ram.Select(m => (int)(J.D(m, "configured") is > 0 ? J.D(m, "configured")! : J.D0(m, "rated"))).ToList();
        bool ddr5 = c.Ram.Any(m => J.D(m, "type") == 34) || speeds.Any(s => s >= 4800);
        int top = speeds.Max();
        bool knownCfg = c.Ram.Any(m => J.D(m, "configured") is > 0);
        if (ddr5 && top >= 5600)
        {
            string origin = knownCfg ? "velocidade configurada" : "velocidade nominal do pente (a configurada não foi lida)";
            bool am5 = Regex.IsMatch(J.S(c.Board, "cpu"), @"Ryzen\s+\d\s+[79]\d{3}");
            c.F("warn", $"Memória rodando/anunciada a {top} MT/s ({origin})",
                "Acima do padrão DDR5 (≈4800 a 5600), o perfil XMP/EXPO costuma estar ativo." +
                (am5 ? " Em CPUs Ryzen 7000/9000 (AM5), esses perfis são uma causa comum de instabilidade." : ""),
                "Teste com XMP/EXPO/DOCP desativado por 2 a 3 dias.");
            if (knownCfg) c.Add("ram", 8, $"Memória configurada a {top} MT/s (perfil EXPO/XMP provável)");
        }
        if (c.Ram.Select(m => (J.S(m, "part"), J.S(m, "gb"))).Distinct().Count() > 1)
            c.F("warn", "Pentes de RAM diferentes entre si",
                Join("; ", c.Ram.Select(m => $"{J.S(m, "slot")}: {J.S(m, "part")} {J.S(m, "gb")}GB")),
                "Pentes de modelos diferentes aumentam o risco de instabilidade.");
    }

    private static void SecWhea(Ctx c)
    {
        c.T("crash", "hw");
        var events = J.List(c.Raw, "ev_whea");
        bool IsCorr(JsonNode? e) => J.D(e, "id") is { } id && Diagnostics.WheaCorrectedIds.Contains((int)id);
        var corrected = events.Where(IsCorr).ToList();
        var whea = events.Where(e => !IsCorr(e)).ToList();
        string Line(JsonNode? e) => $"{J.Dm(J.Ts(e, "t"))}: {Cut(J.S(e, "m"), 160)}";
        if (whea.Count > 0)
        {
            c.F("crit", $"{whea.Count} erros de hardware reportados pelo Windows (WHEA)", Join("\n", whea.Take(4).Select(Line)),
                "Erros WHEA indicam CPU, memória ou barramento PCIe reportando falha ao Windows.");
            c.Add("hw", 20, $"{whea.Count} eventos WHEA");
        }
        if (corrected.Count > 0)
        {
            bool many = corrected.Count >= 20;
            c.F(many ? "warn" : "info", $"{corrected.Count} erros de hardware corrigidos automaticamente (WHEA)",
                Join("\n", corrected.Take(3).Select(Line)),
                many ? "Erros corrigidos são comuns e o hardware se recuperou sozinho; muitos em pouco tempo indicam instabilidade (RAM, PCIe ou overclock)." : "");
            if (many) c.Add("hw", 4, $"{corrected.Count} erros WHEA corrigidos");
        }
        if (events.Count == 0) c.F("ok", "Nenhum erro de hardware WHEA registrado");
    }

    private static readonly HashSet<int> HardDiskEvents = new() { 7, 11, 15, 51, 55, 98, 129, 153, 157 };

    private static void SecDisks(Ctx c)
    {
        c.T("disk", "crash", "perf", "update");
        bool badDisk = false;
        var disks = J.List(c.Raw, "disks");
        foreach (var d in disks)
        {
            string name = Or(J.S(d, "name"), "disco");
            var issues = new List<string>();
            var health = J.S(d, "health");
            if (health != "" && health != "Healthy") { issues.Add($"saúde: {health}"); badDisk = true; }
            if (J.D(d, "temp") is >= 65 and var t) issues.Add($"temperatura {J.F0(t)} °C");
            if (J.D(d, "wear") is >= 80 and var w) issues.Add($"desgaste {J.F0(w)}%");
            if (J.D0(d, "readErr") > 0 || J.D0(d, "writeErr") > 0)
            {
                issues.Add($"erros de leitura/escrita: {J.F0(J.D0(d, "readErr"))}/{J.F0(J.D0(d, "writeErr"))}");
                badDisk = true;
            }
            if (issues.Count > 0) c.F("warn", $"Disco {name}: " + Join(", ", issues), "", "Faça backup e confira o SMART.");
        }
        if (disks.Count > 0 && !badDisk) c.F("ok", "Discos reportam saúde normal (Healthy) e sem erros de leitura/escrita");
        bool lowC = false;
        foreach (var v in J.List(c.Raw, "volumes"))
        {
            double gb = J.D0(v, "gb"), free = J.D0(v, "freeGb");
            if (gb >= 20 && free / gb < 0.10)
            {
                c.F("warn", $"Pouco espaço livre em {J.S(v, "letter")}: {J.F0(free)} GB de {J.F0(gb)} GB ({J.F0(free / gb * 100)}%)",
                    "", "Menos de 10% livre prejudica o desempenho e a atualização do Windows.");
                c.Add("diskslow", 8, $"{J.S(v, "letter")}: com apenas {J.F0(free / gb * 100)}% livre");
            }
            if (J.S(v, "letter").ToUpperInvariant() == "C" && free < 15) lowC = true;
        }
        var hard = J.List(c.Raw, "ev_disk").Where(e => J.D(e, "id") is { } id && HardDiskEvents.Contains((int)id)).ToList();
        if (hard.Count > 0)
        {
            var cnt = J.MostCommon(hard.Select(e => $"{J.S(e, "p")} #{J.F0(J.D0(e, "id"))}"));
            c.F("crit", $"{hard.Count} eventos de erro de disco no registro", Join("\n", cnt.Take(6).Select(k => $"{k.Key} ×{k.Value}")),
                "IDs como 7, 11, 51, 55 e 153 indicam bad block, falha de controladora ou erro de paginação.");
            c.Add("disk", 20, $"{hard.Count} eventos de erro de disco");
        }
        if (badDisk) c.Add("disk", 20, "um disco reporta saúde ruim ou erros de leitura/escrita");
        c.T("crash");
        var dumps = J.List(c.Raw, "ev_dump");
        if (dumps.Count > 0)
            c.F("info", $"Em {dumps.Count} quedas o Windows não conseguiu gravar o dump completo",
                "Costuma acontecer quando o sistema já está muito instável no momento da falha.");
        c.LowC = lowC;
    }

    private static readonly Regex GpuModule = new("nvlddmkm|nvwgf2um|nvd3dum|atiumd|amdxx|d3d11|d3d12", RegexOptions.IgnoreCase);

    private static void SecApps(Ctx c)
    {
        c.T("apps", "mem", "crash");
        var apps = J.List(c.Raw, "ev_apps");
        if (apps.Count > 0)
        {
            var byApp = J.MostCommon(apps.Select(a => Or(J.S(a, "app"), "?")));
            string Code(JsonNode? a) => J.S(a, "code").ToLowerInvariant();
            var memApps = apps.Where(a => Diagnostics.CrashCodes.ContainsKey(Code(a))).Select(a => J.S(a, "app")).Distinct().ToList();
            var heapApps = apps.Where(a => Code(a) == "c0000374").Select(a => J.S(a, "app")).Distinct().ToList();
            var sysApps = memApps.Where(a => Diagnostics.SystemApps.Any(s => a.ToLowerInvariant().StartsWith(s))).ToList();
            var codesN = J.MostCommon(apps.Select(Code));
            var det = new List<string>
            {
                "Mais frequentes: " + Join(", ", byApp.Take(6).Select(k => $"{k.Key} ×{k.Value}")),
                "Códigos: " + Join(", ", codesN.Take(4).Select(k => $"{k.Key} ({(Diagnostics.CrashCodes.TryGetValue(k.Key, out var n) ? n : "outro")}) ×{k.Value}")),
            };
            var (topApp, topN) = (byApp[0].Key, byApp[0].Value);
            if (memApps.Count >= 6 && sysApps.Count >= 2)
            {
                c.F("crit", $"{memApps.Count} programas diferentes travaram com erros de memória, incluindo {sysApps.Count} do próprio Windows",
                    Join("\n", det),
                    "Falhas simultâneas em programas sem relação (inclusive dwm, Defender, svchost) apontam para memória/hardware, não para um app.");
                c.Add("ram", 10, $"{memApps.Count} programas diferentes com falha de memória (acesso inválido/heap), incluindo processos do Windows");
            }
            else if (topN >= 5 && (double)topN / apps.Count >= 0.5)
                c.F("warn", $"{topApp} concentra {topN} de {apps.Count} travamentos", Join("\n", det),
                    $"O problema é provavelmente só desse programa: atualize, repare ou reinstale {topApp}.");
            else
                c.F(apps.Count >= 10 ? "warn" : "info", $"{apps.Count} travamentos de programas em 30 dias", Join("\n", det));
            if (heapApps.Count >= 3) c.Add("ram", 8, $"corrupção de heap (c0000374) em {heapApps.Count} programas diferentes");
            int gpuMods = apps.Count(a => GpuModule.IsMatch(J.S(a, "module")));
            if (gpuMods >= 3) c.Add("gpu", 6, $"{gpuMods} travamentos em módulos de vídeo");
        }
        else c.F("ok", "Nenhum travamento de programa registrado nos últimos 30 dias");
        c.T("apps", "perf");
        var hangs = J.List(c.Raw, "ev_hangs");
        if (hangs.Count > 0)
        {
            var hc = J.MostCommon(hangs.Select(h => Or(J.S(h, "app"), "?")));
            c.F(hangs.Count >= 10 ? "warn" : "info", $"{hangs.Count} vezes um programa deixou de responder",
                "Mais frequentes: " + Join(", ", hc.Take(6).Select(k => $"{k.Key} ×{k.Value}")),
                "Programas que congelam com frequência costumam estar sobrecarregados (CPU/RAM/disco) ou com bug.");
        }
    }

    private static readonly Regex TdrText = new("timeout|TDR|reset", RegexOptions.IgnoreCase);

    private static void SecGpu(Ctx c)
    {
        c.T("gpu", "video", "apps", "crash");
        var gpus = J.List(c.Raw, "gpus");
        var tdr = J.List(c.Raw, "ev_gpu").Where(e => J.D(e, "id") == 4101 || TdrText.IsMatch(J.S(e, "m"))).ToList();
        if (tdr.Count > 0)
        {
            c.F("warn", $"{tdr.Count} eventos de reinício do driver de vídeo (TDR)", "", "Reinstale o driver da GPU com DDU.");
            c.Add("gpu", 8, $"{tdr.Count} eventos de timeout/reset do driver de vídeo");
        }
        else if (gpus.Count > 0) c.F("ok", "Nenhum reinício do driver de vídeo registrado");
        if (c.Codes.Distinct().Count(k => k is 0x116 or 0x117 or 0x119 or 0x141) >= 2)
            c.Add("gpu", 15, "mais de uma tela azul de vídeo (TDR)");
        if (gpus.Count >= 2)
            c.F("info", "Este PC tem mais de uma placa de vídeo (integrada + dedicada)", Join("; ", gpus.Select(g => J.S(g, "name"))),
                "Confirme que o monitor está ligado na saída da placa DEDICADA (não na da placa-mãe) para jogos e desempenho.");
        foreach (var g in gpus)
        {
            var d = J.Ts(g, "date");
            if (d != null && (c.Now - d.Value).Days > 540 && !J.S(g, "name").ToLowerInvariant().Contains("microsoft basic"))
                c.F("info", $"Driver de vídeo antigo: {J.S(g, "name")} ({d.Value:MM/yyyy})", "", "Considere atualizar pelo site da AMD/NVIDIA.");
        }
        c.T("perf", "mem", "apps");
        var ex = J.List(c.Raw, "ev_exhaust");
        if (ex.Count > 0)
        {
            c.F("warn", $"O Windows registrou {ex.Count} vezes falta de memória", "", "Feche programas pesados ou aumente o arquivo de paginação.");
            c.Add("lowram", 10, $"esgotamento de memória registrado {ex.Count} vez(es)");
        }
        c.T("video", "driver", "crash", "perf");
        var pnp = J.List(c.Raw, "pnp");
        if (pnp.Count > 0)
        {
            c.F("warn", $"{pnp.Count} dispositivo(s) com problema no Gerenciador de Dispositivos",
                Join("\n", pnp.Take(6).Select(p => $"{J.S(p, "cls")}: {J.S(p, "name")} ({J.S(p, "status")}, {J.S(p, "problem")})")),
                "Atualize ou reinstale o driver desses dispositivos.");
            c.Add("driver", 4, $"{pnp.Count} dispositivos com problema");
        }
    }

    private static void SecPerf(Ctx c)
    {
        var perf = J.Obj(c.Raw, "perf");
        c.T("perf", "mem");
        double totKb = J.D0(c.Osd, "ramKb"), freeKb = J.D0(c.Osd, "ramFreeKb");
        var topRam = J.List(perf, "topRam");
        var topCpu = J.List(perf, "topCpu").Where(p => !(J.S(p, "name").ToLowerInvariant() is "idle" or "_total")).ToList();
        if (totKb > 0)
        {
            double usedPct = (1 - freeKb / totKb) * 100, usedGb = (totKb - freeKb) / 1048576, totGb = totKb / 1048576;
            string det = topRam.Count > 0
                ? "Maiores consumidores: " + Join(", ", topRam.Take(5).Select(p => $"{J.S(p, "name")} ({J.F0(J.D0(p, "ramMb"))} MB)")) : "";
            var commit = J.D(perf, "commit");
            if (commit != null) det += (det != "" ? "\n" : "") + $"Memória comprometida (commit): {J.F0(commit.Value)}%";
            string sev = usedPct >= 92 ? "crit" : usedPct >= 85 ? "warn" : "ok";
            c.F(sev, $"Memória RAM em uso: {J.F0(usedPct)}% ({J.F1(usedGb)} de {J.F1(totGb)} GB)", det,
                sev != "ok" ? "Feche programas pesados ou reinicie o PC." : "");
            if (usedPct >= 92) c.Add("lowram", 18, $"RAM em {J.F0(usedPct)}% de uso");
            else if (usedPct >= 85) c.Add("lowram", 12, $"RAM em {J.F0(usedPct)}% de uso");
            if (commit >= 90) c.Add("lowram", 8, $"memória comprometida em {J.F0(commit.Value)}%");
        }
        c.T("perf", "thermal");
        if (J.D(perf, "cpuAvg") is { } cpu)
        {
            var heavy = topCpu.Where(p => J.D0(p, "cpu") >= 1).Take(5).ToList();
            string det = heavy.Count > 0
                ? "Maiores consumidores de CPU agora: " + Join(", ", heavy.Select(p => $"{J.S(p, "name")} ({J.F0(J.D0(p, "cpu"))}%)")) : "";
            string sev = cpu >= 90 ? "crit" : cpu >= 75 ? "warn" : "ok";
            c.F(sev, $"CPU em uso médio de {J.F0(cpu)}% durante a coleta", det,
                sev != "ok" ? "Veja no Gerenciador de Tarefas o que está consumindo." : "");
            if (cpu >= 85) c.Add("bgload", 15, $"CPU em {J.F0(cpu)}% de uso médio");
            else if (cpu >= 70) c.Add("bgload", 8, $"CPU em {J.F0(cpu)}% de uso médio");
            if (topCpu.Count > 0 && J.D0(topCpu[0], "cpu") >= 25)
                c.Add("bgload", 6, $"{J.S(topCpu[0], "name")} usando {J.F0(J.D0(topCpu[0], "cpu"))}% da CPU");
        }
        c.T("disk", "perf");
        if (J.D(perf, "diskQueue") is { } q)
        {
            double lat = J.D0(perf, "diskLatMs"), busy = J.D0(perf, "diskBusy");
            if (q >= 2 || lat >= 30 || busy >= 90)
            {
                c.F("warn", "Disco ocupado ou lento durante a coleta",
                    $"Fila média: {J.F1(q)}; latência: {J.F0(lat)} ms; tempo ocupado: {J.F0(busy)}%.",
                    "Veja no Gerenciador de Tarefas qual programa usa o disco; HDD costuma ser o gargalo.");
                c.Add("diskslow", 15, $"disco ocupado (fila {J.F1(q)}, latência {J.F0(lat)} ms, {J.F0(busy)}% ocupado)");
            }
            else c.F("ok", $"Disco sem sobrecarga (latência {J.F0(lat)} ms, ocupado {J.F0(busy)}%)");
        }
        c.T("perf", "update");
        var boot = J.Ts(c.Osd, "boot");
        if (boot != null && (c.Now - boot.Value).Days >= 14)
            c.F("info", $"O PC está ligado há {(c.Now - boot.Value).Days} dias sem reiniciar", "", "Reiniciar libera memória e conclui atualizações pendentes.");
        c.T("startup", "perf");
        var st = J.List(c.Raw, "startup");
        if (st.Count > 0)
        {
            int n = st.Count;
            string sev = n >= 15 ? "warn" : n >= 8 ? "info" : "ok";
            var names = st.Select(s => J.S(s, "name")).Distinct().OrderBy(s => s, StringComparer.Ordinal).Take(14);
            c.F(sev, $"{n} programas iniciam com o Windows", Join(", ", names),
                sev != "ok" ? "Desative os desnecessários em Gerenciador de Tarefas > Inicializar." : "");
            if (n >= 20) c.Add("startup", 10, $"{n} programas na inicialização");
            else if (n >= 12) c.Add("startup", 6, $"{n} programas na inicialização");
        }
    }

    private static void SecThermal(Ctx c)
    {
        c.T("thermal", "perf", "power");
        var r = c.Recent;
        if (r != null && r.Rows >= 30)
        {
            double hours = r.Hours > 0 ? r.Hours : r.Rows * 5 / 3600.0;
            foreach (var (label, key, warn, crit, pts) in new[] { ("CPU", "cpu.temp", 88, 95, (14, 25)), ("GPU", "nv0.temp", 85, 92, (10, 20)) })
            {
                if (!r.Temps.TryGetValue(key, out var t)) continue;
                string sev = t.Max >= crit ? "crit" : t.Max >= warn ? "warn" : "ok";
                c.F(sev, $"Temperatura da {label}: máx. {J.F0(t.Max)} °C, média {J.F0(t.Avg)} °C (últimas ~{J.F0(hours)} h)",
                    "Baseado nas leituras gravadas pelo monitor.", sev != "ok" ? "Verifique limpeza, pasta térmica e ventoinhas." : "");
                if (sev != "ok") c.Add("thermal", sev == "crit" ? pts.Item2 : pts.Item1, $"{label} chegou a {J.F0(t.Max)} °C");
            }
        }
        else
            c.F("info", "Sem dados suficientes de temperatura do monitor",
                "Deixe o monitor rodando algumas horas (inclusive durante o problema) para avaliar o calor.");
    }

    private static bool IsTrue(string s) => s.Equals("true", StringComparison.OrdinalIgnoreCase);
    private static bool IsFalse(string s) => s.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static void SecSecurity(Ctx c)
    {
        c.T("security", "perf");
        var sec = J.Obj(c.Raw, "security");
        if (sec.Count > 0)
        {
            var probs = new List<string>();
            // Campo ausente/nulo = não foi possível ler (não conta como "desativado")
            if (J.S(sec, "av") != "" && !IsTrue(J.S(sec, "av"))) probs.Add("antivírus do Windows desativado");
            if (J.S(sec, "rtp") != "" && !IsTrue(J.S(sec, "rtp"))) probs.Add("proteção em tempo real desativada");
            if (J.D(sec, "sigAge") is >= 7 and var age) probs.Add($"definições de vírus com {J.F0(age)} dias");
            var off = J.List(sec, "fw").Where(f => IsFalse(J.S(f, "on"))).Select(f => J.S(f, "name")).ToList();
            if (off.Count > 0) probs.Add("firewall desativado em: " + Join(", ", off));
            if (probs.Count > 0)
            {
                c.F("warn", "Proteção do Windows com pendências", Join("; ", probs),
                    "Se você usa outro antivírus, isso pode ser normal; senão, ative a proteção em Segurança do Windows.");
                c.Add("security", 8, Join("; ", probs));
            }
            else c.F("ok", "Antivírus e firewall do Windows ativos e atualizados");
        }
        var threats = J.List(c.Raw, "threats");
        if (threats.Count > 0)
        {
            c.F("crit", $"O Defender detectou {threats.Count} ameaça(s) nos últimos 30 dias",
                Join(", ", threats.Take(5).Select(t => $"{J.Dm(J.Ts(t, "t"))} (ID {J.S(t, "id")})")),
                "Abra Segurança do Windows > Proteção contra vírus > Histórico de proteção e remova o que estiver pendente.");
            c.Add("security", 15, $"{threats.Count} detecção(ões) de ameaça");
        }
    }

    private static readonly HashSet<string> InsiderRings = new()
    { "dev", "beta", "canary", "releasepreview", "rp", "wis", "wif", "external", "internal" };

    private static void SecOs(Ctx c)
    {
        c.T("os", "crash", "update", "apps");
        string ring = J.S(c.Osd, "ring").Trim().ToLowerInvariant();
        string flight = J.S(c.Osd, "flight").Trim().ToLowerInvariant();
        bool insider = flight is "1" or "true" || InsiderRings.Contains(ring);
        if (insider)
        {
            c.F("warn", $"Windows em canal Insider ({Or(Or(J.S(c.Osd, "ring"), J.S(c.Osd, "branch")), "teste")}) — build {c.Build}",
                "Builds de teste podem trazer bugs de kernel e drivers.",
                "Se os problemas continuarem depois de corrigir o resto, considere uma versão estável.");
            c.Add("os", 8, $"Windows Insider (build {c.Build})");
        }
        c.T("crash");
        var bd = c.Board.Count > 0 ? J.Ts(c.Board, "biosDate") : null;
        if (bd != null && (c.Now - bd.Value).Days > 365)
            c.F("info", $"BIOS com mais de 1 ano ({J.S(c.Board, "biosVersion")}, {bd.Value:MM/yyyy})", "",
                "Veja no site do fabricante se há versão nova; atualizações costumam melhorar a compatibilidade de memória.");
        else if (c.Board.Count > 0)
            c.F("info", $"BIOS {J.S(c.Board, "biosVersion")} ({(bd?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "?")})");
        c.T("crash", "perf");
        var pw = J.Obj(c.Raw, "power");
        if (J.D(pw, "fastStartup") == 1)
            c.F("info", "Inicialização rápida do Windows está ativada", "Ela mantém o estado do kernel/drivers entre desligamentos.",
                "Para testes de estabilidade, desative (Opções de energia > Escolher a função dos botões).");
        c.Pw = pw;
    }

    private static void SecUpdate(Ctx c)
    {
        c.T("update");
        var upd = J.Obj(c.Raw, "update");
        if (J.S(upd, "wuStart").ToLowerInvariant() == "disabled")
        {
            c.F("warn", "O serviço do Windows Update está desativado", "", "Ative o serviço 'Windows Update' em services.msc.");
            c.Add("update", 10, "serviço Windows Update desativado");
        }
        var uerr = J.List(c.Raw, "ev_update");
        if (uerr.Count >= 3)
        {
            c.F("warn", $"{uerr.Count} falhas do Windows Update nos últimos 30 dias",
                Join("\n", uerr.Take(3).Select(e => $"{J.Dm(J.Ts(e, "t"))}: {Cut(J.S(e, "m"), 140)}")),
                "Use a solução de problemas do Windows Update e rode DISM/SFC.");
            c.Add("update", 12, $"{uerr.Count} falhas de atualização");
        }
        if (c.LowC)
        {
            c.F("warn", "Menos de 15 GB livres no disco C:", "", "O Windows Update precisa de espaço livre para instalar.");
            c.Add("update", 6, "pouco espaço livre no C:");
        }
        if (J.B(c.Pw, "pendingReboot") == true)
        {
            c.F("info", "Há reinicialização pendente de atualização", "", "Reinicie o PC.");
            c.Add("update", 3, "reinicialização pendente");
        }
        var hf = J.List(c.Raw, "hotfix").Select(h => J.Ts(h, "on")).Where(h => h != null).Select(h => h!.Value).ToList();
        if (hf.Count > 0 && (c.Now - hf.Max()).Days > 120)
            c.F("info", $"Última atualização instalada há {(c.Now - hf.Max()).Days} dias", "", "Rode o Windows Update.");
    }

    private static double Loss(JsonNode? p)
    {
        double sent = J.D0(p, "sent"); if (sent == 0) sent = 1;
        return 100.0 * (1 - J.D0(p, "ok") / sent);
    }

    /// <summary>"866 Mbps" / "1 Gbps" / "100 Kbps" em Mbps.</summary>
    public static double? ParseMbps(string? s)
    {
        var m = Regex.Match(s ?? "", @"([\d.,]+)\s*([GMK])bps", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        return v * char.ToUpperInvariant(m.Groups[2].Value[0]) switch { 'G' => 1000.0, 'M' => 1.0, _ => 0.001 };
    }

    private static void SecNet(Ctx c)
    {
        c.T("net");
        var net = J.Obj(c.Raw, "net");
        if (net.Count == 0) return;
        var ads = J.List(net, "adapters");
        var up = ads.Where(a => J.S(a, "status").ToLowerInvariant() == "up").ToList();
        if (ads.Count > 0 && up.Count == 0)
        {
            c.F("crit", "Nenhum adaptador de rede está conectado", Join("; ", ads.Select(a => $"{J.S(a, "name")}: {J.S(a, "status")}")),
                "Confira o cabo, o Wi-Fi e o modo avião.");
            c.Add("netadapter", 25, "nenhum adaptador de rede conectado");
        }
        foreach (var a in up)
        {
            var sp = ParseMbps(J.S(a, "speed"));
            if (J.S(a, "media").Contains("802.3") && sp is > 0 and <= 100)
            {
                c.F("warn", $"Cabo de rede negociou só {J.F0(sp.Value)} Mbps ({J.S(a, "name")})", "", "Cabo ruim ou porta antiga: troque o cabo (Cat5e/Cat6).");
                c.Add("netlocal", 8, $"cabo a {J.F0(sp.Value)} Mbps");
            }
        }
        if (J.D(net, "wifiSignal") is { } sig)
        {
            if (sig < 40)
            {
                c.F("warn", $"Sinal do Wi-Fi fraco ({J.F0(sig)}%)", "", "Aproxime-se do roteador, use 5 GHz ou cabo.");
                c.Add("netlocal", 12, $"sinal Wi-Fi em {J.F0(sig)}%");
            }
            else if (sig < 60) c.F("info", $"Sinal do Wi-Fi mediano ({J.F0(sig)}%)");
        }
        string gw = J.S(net, "gateway");
        var g = J.Get(net, "pingGw") as JsonObject; var p1 = J.Get(net, "pingNet1") as JsonObject; var p2 = J.Get(net, "pingNet2") as JsonObject;
        if (up.Count > 0 && gw == "")
        {
            c.F("warn", "O PC não recebeu gateway/IP da rede", "", "Reinicie o roteador e renove o IP (ipconfig /renew).");
            c.Add("netadapter", 15, "sem gateway padrão");
        }
        bool gwOk = g != null && J.D0(g, "ok") > 0;
        if (g != null && J.D0(g, "ok") == 0 && up.Count > 0)
        {
            c.F("crit", $"O roteador ({gw}) não responde", "", "Reinicie o roteador e confira cabo/Wi-Fi.");
            c.Add("netlocal", 25, $"roteador {gw} sem resposta");
        }
        else if (g != null && Loss(g) >= 20)
        {
            c.F("warn", $"Perda de pacotes até o roteador ({J.F0(Loss(g))}%)", "", "Wi-Fi com interferência ou cabo ruim.");
            c.Add("netlocal", 15, $"{J.F0(Loss(g))}% de perda até o roteador");
        }
        if (g != null && J.D(g, "avg") is > 30 and var gavg)
        {
            c.F("warn", $"Latência alta até o roteador ({J.F0(gavg)} ms)", "", "Em rede local deveria ficar abaixo de 10 ms.");
            c.Add("netlocal", 8, $"latência de {J.F0(gavg)} ms até o roteador");
        }
        var inet = new[] { p1, p2 }.Where(p => p != null).Select(p => p!).ToList();
        bool inetOk = inet.Any(p => J.D0(p, "ok") > 0);
        if (inet.Count > 0 && !inetOk && gwOk)
        {
            c.F("crit", "Sem acesso à internet (o roteador responde)", "Os testes para 1.1.1.1 e 8.8.8.8 falharam.",
                "Reinicie modem e roteador; se continuar, é o provedor.");
            c.Add("netisp", 25, "roteador responde, mas 1.1.1.1 e 8.8.8.8 não");
        }
        else if (inetOk)
        {
            double loss = inet.Max(Loss);
            var lat = inet.Select(p => J.D(p, "avg")).Where(a => a is > 0).Select(a => a!.Value).ToList();
            double latAvg = lat.Count > 0 ? lat.Average() : 0;
            if (loss >= 20)
            {
                c.F("warn", $"Perda de pacotes para a internet ({J.F0(loss)}%)", "", "Instabilidade no provedor ou no Wi-Fi.");
                c.Add("netisp", 12, $"{J.F0(loss)}% de perda para a internet");
            }
            else if (latAvg > 120)
            {
                c.F("warn", $"Latência alta para a internet ({J.F0(latAvg)} ms)", "", "Pode ser congestionamento ou Wi-Fi ruim.");
                c.Add("netisp", 8, $"latência de {J.F0(latAvg)} ms");
            }
            else c.F("ok", $"Internet respondendo (latência média {J.F0(latAvg)} ms, perda {J.F0(loss)}%)");
        }
        var dnsOk = J.B(net, "dnsOk");
        double dnsMs = J.D0(net, "dnsMs");
        if (inetOk && dnsOk == false)
        {
            c.F("crit", "O DNS não está resolvendo nomes (sites não abrem, mas há conexão)", "", "Troque o DNS para 1.1.1.1 e 8.8.8.8.");
            c.Add("netdns", 25, "ping funciona, mas a resolução de nomes falha");
        }
        else if (dnsOk == true && dnsMs > 300)
        {
            c.F("warn", $"DNS lento ({J.F0(dnsMs)} ms)", "", "Troque para 1.1.1.1 / 8.8.8.8.");
            c.Add("netdns", 8, $"DNS levou {J.F0(dnsMs)} ms");
        }
        else if (dnsOk == true) c.F("ok", $"DNS funcionando ({J.F0(dnsMs)} ms)");
    }

    private static void SecHistory(Ctx c)
    {
        c.T("thermal", "crash", "power");
        var hist = c.HistoryInfo;
        if (hist == null || hist.Count == 0) return;
        var temps = new List<double>(); var rams = new List<double>();
        foreach (var h in hist)
        {
            foreach (var k in new[] { "cpu.temp", "nv0.temp" }) if (h.Values.TryGetValue(k, out var t)) temps.Add(t);
            if (h.Values.TryGetValue("ram.pct", out var r)) rams.Add(r);
        }
        if (temps.Count > 0)
        {
            double hot = temps.Max();
            if (hot >= 90)
            {
                c.F("crit", $"Antes de uma queda o monitor registrou {J.F0(hot)} °C",
                    $"Baseado em {hist.Count} sessões encerradas de forma inesperada.", "Verifique refrigeração.");
                c.Add("thermal", 20, $"temperatura de {J.F0(hot)} °C registrada logo antes de uma queda");
            }
            else
                c.F("ok", $"Temperaturas normais antes das quedas (máx. {J.F0(hot)} °C nos últimos registros)",
                    $"Baseado em {hist.Count} sessões do monitor. Superaquecimento é improvável.");
        }
        if (rams.Count > 0)
        {
            double mx = rams.Max();
            c.F(mx < 85 ? "ok" : "warn", $"RAM em uso antes das quedas: máx. {J.F0(mx)}%",
                mx < 85 ? "Falta de memória é improvável." : "Uso alto de memória no momento da queda.");
        }
    }

    private static void SecMilestone(Ctx c)
    {
        c.T("crash", "power", "mem");
        var ms = c.Milestone;
        if (ms == null) return;
        double hours = (c.Now - ms.Time).TotalSeconds / 3600;
        var after = c.Power.Where(t => t >= ms.Time).ToList();
        string note = ms.Note != "" ? ms.Note : "mudança";
        if (after.Count == 0 && hours >= 24)
            c.F("ok", $"Sem quedas há {J.F0(hours)} h desde '{note}' ({J.Dm(ms.Time)})", "Bom sinal: continue acompanhando por alguns dias para confirmar.");
        else
        {
            var before = c.Power.Where(t => t >= ms.Time.AddDays(-7) && t < ms.Time).ToList();
            c.F(after.Count == 0 ? "info" : "warn", $"Desde '{note}' ({J.Dm(ms.Time)}): {after.Count} queda(s) em {J.F0(hours)} h",
                $"Nos 7 dias antes da mudança: {before.Count} queda(s) (≈ {J.F1(before.Count / 168.0 * 24)}/dia). " +
                $"Depois: ≈ {J.F1(after.Count / Math.Max(hours, 1) * 24)}/dia.");
        }
    }

    /// <summary>Separa achados do foco dos demais e ordena as hipóteses.</summary>
    private static DiagnosticReport Finish(Ctx c)
    {
        var rep = c.Rep; var focus = Diagnostics.Scenarios[c.Scenario].Focus;
        rep.Findings = rep.Findings.OrderBy(f => Diagnostics.SevOrder[f.Sev]).ToList();
        if (focus == null) { rep.Relevant = rep.Findings.ToList(); rep.Others = new(); }
        else
        {
            rep.Relevant = rep.Findings.Where(f => f.Tags.Overlaps(focus)).ToList();
            rep.Others = rep.Findings.Where(f => !f.Tags.Overlaps(focus)).ToList();
        }
        rep.Hypotheses = Diagnostics.HypOrder
            .Where(k => c.Score.GetValueOrDefault(k) > 0 && (focus == null || Diagnostics.HypTags[k].Overlaps(focus)))
            .Select(k => new Hypothesis(k, Diagnostics.HypNames[k], c.Score[k], c.Evid[k], Diagnostics.HypAdvice[k]))
            .OrderByDescending(h => h.Score).ToList();
        if (rep.Hypotheses.Count > 0)
        {
            int s = rep.Hypotheses[0].Score;
            rep.Confidence = s >= 30 ? "alta" : s >= 18 ? "média" : "baixa";
        }
        return rep;
    }
}
