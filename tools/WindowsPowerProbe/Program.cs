using System.Text.Json;
using HonorControl.Contracts;
using HonorControl.Services;

// No switch invokes real power writes. --read-only uses only native enumeration/read calls.
if (args.SequenceEqual(new[] { "--read-only" }))
{
    Console.WriteLine(JsonSerializer.Serialize(new WindowsPowerSettingsService(new WindowsPowerApi(), new MemoryStore()).Get(),
        new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (args.Length != 0) throw new ArgumentException("Use no arguments for simulation or --read-only for native reads.");

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (ArgumentException) { Check(true, name); return; }
    throw new InvalidOperationException("FAIL: " + name);
}
WindowsPowerSettingsService.Validate(new(AcDisplayTimeoutSeconds: 0, DcSleepTimeoutSeconds: 86400,
    AcIntelGraphicsPowerPlan: 0, DcIntelGraphicsPowerPlan: 2));
Check(true, "timeout and Intel enum boundaries accepted");
Reject(() => WindowsPowerSettingsService.Validate(new(DcSleepTimeoutSeconds: 86401)), "timeout above maximum rejected");
Reject(() => WindowsPowerSettingsService.Validate(new(DcDiskTimeoutSeconds: 86401)), "disk timeout above maximum rejected");
Reject(() => WindowsPowerSettingsService.Validate(new(AcIntelGraphicsPowerPlan: -1)), "negative Intel enum rejected");
Reject(() => WindowsPowerSettingsService.Validate(new(DcIntelGraphicsPowerPlan: 3)), "unknown Intel enum rejected");
Reject(() => WindowsPowerSettingsService.Validate(new(SchemeId: "invalid")), "invalid plan ID rejected");

FakeApi api = new();
MemoryStore store = new();
WindowsPowerSettingsService service = new(api, store);
service.Set(new(DcDisplayTimeoutSeconds: 300, ExpectedActiveSchemeId: api.Active.ToString()));
Check(api.Writes.Count == 1 && api.Writes[0].Name == "DcDisplayTimeoutSeconds", "only submitted DC display field written");
Check(store.Journal?.Changes.Single().Original == 600, "original persisted before write");
service.Set(new(DcDisplayTimeoutSeconds: 120));
Check(store.Journal?.Changes.Single().Original == 600, "repeated edits preserve earliest original");
service.Restore();
Check(api.Values[(api.Active, "DcDisplayTimeoutSeconds")] == 600 && store.Journal == null, "explicit restore returns original and clears journal");

api = new(); store = new(); service = new(api, store);
api.FailOnWrite = 2;
try { service.Set(new(AcDisplayTimeoutSeconds: 300, DcDisplayTimeoutSeconds: 120)); }
catch (InvalidOperationException) { }
Check(api.Values[(api.Active, "AcDisplayTimeoutSeconds")] == 600
    && api.Values[(api.Active, "DcDisplayTimeoutSeconds")] == 600 && store.Journal == null,
    "partial write failure rolls back attempted fields and journal");

api = new(); store = new(); service = new(api, store);
Guid originalPlan = api.Active;
api.FailActivationOnce = true;
try { service.Set(new(SchemeId: FakeApi.Other.ToString(), DcSleepTimeoutSeconds: 1800)); }
catch (InvalidOperationException) { }
Check(api.Active == originalPlan && api.Values[(FakeApi.Other, "DcSleepTimeoutSeconds")] == 600
    && store.Journal == null, "activation failure restores selected-plan field and original active scheme");

api = new(); store = new(); service = new(api, store);
service.Set(new(DcDisplayTimeoutSeconds: 300));
api.Values[(api.Active, "DcDisplayTimeoutSeconds")] = 999;
int count = api.Writes.Count;
try { service.Restore(); }
catch (InvalidOperationException) { }
Check(api.Writes.Count == count && store.Journal != null, "restore refuses external modification and keeps backup");

api = new(); store = new(); service = new(api, store);
api.UnsupportedIntel = true;
WindowsPowerSettingsSnapshot snapshot = service.Get();
Check(snapshot.AcIntelGraphicsPowerPlan == null && snapshot.MissingReason?.ContainsKey("AcIntelGraphicsPowerPlan") == true,
    "unsupported setting has null and explicit reason");
try { service.Set(new(AcIntelGraphicsPowerPlan: 1)); }
catch (InvalidOperationException) { }
Check(api.Writes.Count == 0 && store.Journal == null, "unsupported Intel setting rejected before backup/write");

api = new(); store = new() { FailSave = true }; service = new(api, store);
try { service.Set(new(DcDisplayTimeoutSeconds: 300)); }
catch (IOException) { }
Check(api.Writes.Count == 0, "backup persistence failure prevents all power writes");

api = new(); store = new(); service = new(api, store);
try { service.Set(new(DcDisplayTimeoutSeconds: 300, ExpectedActiveSchemeId: FakeApi.Other.ToString())); }
catch (InvalidOperationException) { }
Check(api.Writes.Count == 0 && store.Journal == null, "stale active-plan guard rejects before backup/write");

api = new(); store = new(); service = new(api, store);
originalPlan = api.Active;
service.Set(new(SchemeId: FakeApi.Other.ToString()));
Check(api.Active == FakeApi.Other && api.Writes.Count == 0, "plan selection does not rewrite any setting");
service.Restore();
Check(api.Active == originalPlan && store.Journal == null, "plan-only restore returns previous active plan");

api = new(); store = new(); service = new(api, store);
api.FailWritesFrom = 2;
try { service.Set(new(AcDisplayTimeoutSeconds: 300, DcDisplayTimeoutSeconds: 120)); }
catch (InvalidOperationException) { }
Check(store.Journal != null && api.Values[(api.Active, "AcDisplayTimeoutSeconds")] == 300,
    "rollback failure preserves journal for explicit recovery");
api.FailWritesFrom = 0;
service.Restore();
Check(store.Journal == null && api.Values[(api.Active, "AcDisplayTimeoutSeconds")] == 600,
    "explicit restore can recover a partial failed update");

api = new(); store = new(); service = new(api, store);
service.Set(new(DcDiskTimeoutSeconds: 1200));
Check(api.Writes.Count == 1 && api.Writes[0].Name == "DcDiskTimeoutSeconds"
    && api.Values[(api.Active, "DcDiskTimeoutSeconds")] == 1200
    && api.Values[(api.Active, "AcDiskTimeoutSeconds")] == 600,
    "only submitted DC disk timeout written; AC and other settings untouched");
snapshot = service.Get();
Check(snapshot.AcDiskTimeoutSeconds == 600 && snapshot.DcDiskTimeoutSeconds == 1200,
    "disk timeouts above enum range read as seconds rather than Intel policies");
service.Restore();
Check(api.Values[(api.Active, "DcDiskTimeoutSeconds")] == 600 && store.Journal == null,
    "disk timeout restored to original with backup cleared");
Console.WriteLine($"{passed} simulations passed; no real Windows power settings changed.");

internal sealed class MemoryStore : IWindowsPowerJournalStore
{
    public PowerJournal? Journal;
    public bool FailSave;
    public PowerJournal? Load() => Journal;
    public void Save(PowerJournal? journal)
    {
        if (FailSave) throw new IOException("simulated backup failure");
        Journal = journal;
    }
}
internal sealed class FakeApi : IWindowsPowerApi
{
    public static readonly Guid Other = new("11111111-2222-3333-4444-555555555555");
    public Guid Active = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public Dictionary<(Guid, string), uint> Values = [];
    public List<PowerSetting> Writes = [];
    public int FailOnWrite;
    public int FailWritesFrom;
    public bool FailActivationOnce, UnsupportedIntel;
    public FakeApi()
    {
        foreach (Guid scheme in new[] { Active, Other })
            foreach (string name in new[] { "AcDisplayTimeoutSeconds", "DcDisplayTimeoutSeconds", "AcSleepTimeoutSeconds", "DcSleepTimeoutSeconds",
                "AcIntelGraphicsPowerPlan", "DcIntelGraphicsPowerPlan", "AcDiskTimeoutSeconds", "DcDiskTimeoutSeconds" })
                Values[(scheme, name)] = name.Contains("Intel") ? 1u : 600u;
    }
    public Guid GetActive() => Active;
    public IReadOnlyList<WindowsPowerScheme> Enumerate() => [new(Active.ToString(), "active"), new(Other.ToString(), "other")];
    public uint Read(Guid scheme, PowerSetting setting)
    {
        if (UnsupportedIntel && setting.Name.Contains("Intel")) throw new InvalidOperationException("not supported");
        return Values[(scheme, setting.Name)];
    }
    public void Write(Guid scheme, PowerSetting setting, uint value)
    {
        Writes.Add(setting);
        if (Writes.Count == FailOnWrite || FailWritesFrom > 0 && Writes.Count >= FailWritesFrom)
            throw new InvalidOperationException("simulated write failure");
        Values[(scheme, setting.Name)] = value;
    }
    public void SetActive(Guid scheme)
    {
        Active = scheme;
        if (FailActivationOnce) { FailActivationOnce = false; throw new InvalidOperationException("simulated activation failure after apply"); }
    }
}
